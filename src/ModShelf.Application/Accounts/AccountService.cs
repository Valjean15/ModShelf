using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Links, lists and unlinks accounts, and hands out their credentials to requests, refreshing them as needed.
/// </summary>
/// <remarks>
/// <para>
/// One class for both roles, so that linking and refreshing share one copy of the credentials and one lock per
/// source: a refresh never overwrites an account that was just linked again, and two requests refused at the
/// same moment cause one refresh, not two. Register one instance as both <see cref="IAccountService"/> and
/// <see cref="ICredentialProvider"/>.
/// </para>
/// <para>
/// Credentials go to the <see cref="ISecretStore"/>; who the account belongs to goes to the
/// <see cref="ILinkedAccountRepository"/>. Logs name sources and kinds only, never credentials or answers.
/// </para>
/// </remarks>
internal sealed partial class AccountService : IAccountService, ICredentialProvider
{
    private readonly ISourceRegistry _sources;
    private readonly ILinkedAccountRepository _accounts;
    private readonly IOAuthRedirectListener _redirects;
    private readonly IBrowserLauncher _browser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountService> _logger;
    private readonly CredentialVault _vault;

    public AccountService(
        ISourceRegistry sources,
        ILinkedAccountRepository accounts,
        ISecretStore secrets,
        IOAuthRedirectListener redirects,
        IBrowserLauncher browser,
        TimeProvider timeProvider,
        ILogger<AccountService> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(redirects);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _accounts = accounts;
        _redirects = redirects;
        _browser = browser;
        _timeProvider = timeProvider;
        _logger = logger;
        _vault = new CredentialVault(secrets, logger);
    }

    public async Task<IReadOnlyList<SourceAccountStatus>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LinkedAccount> linked = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. _sources.All<IAccountLinker>().Select(source =>
            {
                LinkedAccount? account = linked.FirstOrDefault(account => account.Source == source.Id);
                return new SourceAccountStatus(
                    source.Id,
                    source.DisplayName,
                    source.WorksWithoutAccount,
                    [.. source.Strategies.Select(strategy => strategy.Kind)],
                    account,
                    account is null ? null : (source as IApiQuota)?.Quota);
            }),
        ];
    }

    public async Task<LinkedAccount> CheckAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        IAccountLinker linker = _sources.Find<IAccountLinker>(source)
            ?? throw new KeyNotFoundException($"No registered source '{source}' can link accounts.");
        LinkedAccount account = (await _accounts.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(linked => linked.Source == source)
            ?? throw new KeyNotFoundException($"No account is linked on {source}.");
        VaultEntry entry = await GetUsableAsync(source, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthenticationFailedException($"The credentials stored for {source} can't be used any more.");

        // The linker sends the credentials it's given, as when linking, so the app leaves its requests alone.
        AccountProfile profile = await WhileManagingAsync(
                source,
                () => linker.GetProfileAsync(entry.Credentials, entry.Kind, cancellationToken))
            .ConfigureAwait(false);
        LinkedAccount checkedAccount = new(source, entry.Kind, profile, account.LinkedAt);
        await _accounts.SaveAsync(checkedAccount, CancellationToken.None).ConfigureAwait(false);
        LogChecked(_logger, source);
        return checkedAccount;
    }

    public async Task<LinkedAccount> LinkAsync(SourceId source, AuthKind kind, IAuthPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prompt);

        IAccountLinker linker = _sources.Find<IAccountLinker>(source)
            ?? throw new KeyNotFoundException($"No registered source '{source}' can link accounts.");
        IAuthStrategy strategy = linker.Strategies.FirstOrDefault(offered => offered.Kind == kind)
            ?? throw new ArgumentException($"Source '{source}' doesn't offer {kind}.", nameof(kind));

        (AccountCredentials credentials, AccountProfile profile) = await WhileManagingAsync(
                source,
                () => GetCredentialsAndProfileAsync(source, linker, strategy, prompt, cancellationToken))
            .ConfigureAwait(false);
        LinkedAccount account = new(source, kind, profile, _timeProvider.GetUtcNow());

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            // Past this point the account is linked: finishing both writes matters more than cancelling.
            await _vault.SaveAsync(source, new VaultEntry(kind, credentials), CancellationToken.None).ConfigureAwait(false);
            await _accounts.SaveAsync(account, CancellationToken.None).ConfigureAwait(false);
        }

        LogLinked(_logger, source, kind);
        return account;
    }

    public async Task UnlinkAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            // The secret goes first: if removing it fails, the account stays listed, so unlinking can be retried.
            await _vault.DeleteAsync(source, CancellationToken.None).ConfigureAwait(false);
            await _accounts.DeleteAsync(source, CancellationToken.None).ConfigureAwait(false);
        }

        LogUnlinked(_logger, source);
    }

    private static async Task<AccountCredentials> LinkWithApiKeyAsync(
        SourceId source,
        IAuthStrategy strategy,
        IAuthPrompt prompt,
        CancellationToken cancellationToken)
    {
        AuthChallenge challenge = await strategy.BeginAsync(new AuthStart(), cancellationToken).ConfigureAwait(false);
        string key = Entered(await prompt.AskApiKeyAsync(source, challenge.Url, cancellationToken).ConfigureAwait(false), "No API key was entered.");
        return await strategy.CompleteAsync(challenge, key, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AccountCredentials> LinkWithEmailedCodeAsync(
        SourceId source,
        IAuthStrategy strategy,
        IAuthPrompt prompt,
        CancellationToken cancellationToken)
    {
        string email = await prompt.AskEmailAsync(source, cancellationToken).ConfigureAwait(false);
        AuthChallenge challenge = await strategy.BeginAsync(new AuthStart(email: email), cancellationToken).ConfigureAwait(false);
        string code = Entered(await prompt.AskEmailCodeAsync(source, challenge.SentTo, cancellationToken).ConfigureAwait(false), "No code was entered.");
        return await strategy.CompleteAsync(challenge, code, cancellationToken).ConfigureAwait(false);
    }

    private static string Entered(string answer, string messageWhenBlank)
        => string.IsNullOrWhiteSpace(answer) ? throw new ArgumentException(messageWhenBlank, nameof(answer)) : answer.Trim();

    /// <summary>
    /// Gets credentials through the strategy and the prompt, then looks up whose account they belong to.
    /// </summary>
    private async Task<(AccountCredentials Credentials, AccountProfile Profile)> GetCredentialsAndProfileAsync(
        SourceId source,
        IAccountLinker linker,
        IAuthStrategy strategy,
        IAuthPrompt prompt,
        CancellationToken cancellationToken)
    {
        AccountCredentials credentials = strategy.Kind switch
        {
            AuthKind.ApiKey => await LinkWithApiKeyAsync(source, strategy, prompt, cancellationToken).ConfigureAwait(false),
            AuthKind.EmailCode => await LinkWithEmailedCodeAsync(source, strategy, prompt, cancellationToken).ConfigureAwait(false),
            AuthKind.OAuthPkce => await LinkInBrowserAsync(source, strategy, prompt, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy.Kind, "Unknown kind of authentication."),
        };
        AccountProfile profile = await linker.GetProfileAsync(credentials, strategy.Kind, cancellationToken).ConfigureAwait(false);
        return (credentials, profile);
    }

    private async Task<AccountCredentials> LinkInBrowserAsync(
        SourceId source,
        IAuthStrategy strategy,
        IAuthPrompt prompt,
        CancellationToken cancellationToken)
    {
        IOAuthRedirect redirect = await _redirects.StartAsync(cancellationToken).ConfigureAwait(false);
        await using (redirect.ConfigureAwait(false))
        {
            AuthChallenge challenge = await strategy.BeginAsync(new AuthStart(redirectUri: redirect.RedirectUri), cancellationToken)
                .ConfigureAwait(false);
            Uri page = challenge.Url
                ?? throw new InvalidOperationException($"Source '{source}' gave no page to approve ModShelf on.");

            bool opened = await _browser.TryOpenAsync(page, cancellationToken).ConfigureAwait(false);
            prompt.ShowBrowserStep(source, page, opened);

            Uri returned = await redirect.WaitForRedirectAsync(cancellationToken).ConfigureAwait(false);
            return await strategy.CompleteAsync(challenge, returned.AbsoluteUri, cancellationToken).ConfigureAwait(false);
        }
    }
}
