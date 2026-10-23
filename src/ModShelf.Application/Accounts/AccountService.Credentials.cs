using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// The <see cref="ICredentialProvider"/> half: current credentials for requests, and refreshing them.
/// </summary>
internal sealed partial class AccountService
{
    /// <summary>
    /// Credentials this close to expiring are refreshed before use, so a request doesn't start with a token
    /// that runs out on the way.
    /// </summary>
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);

    public async Task<AccountCredentials?> GetAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Most requests find good credentials in memory and never wait for the lock.
        if (_vault.TryGetCached(source, out VaultEntry? cached) && Usable(cached) is { } fresh && !IsAboutToExpire(fresh))
        {
            return fresh;
        }

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            VaultEntry? entry = await _vault.GetAsync(source, cancellationToken).ConfigureAwait(false);
            if (Usable(entry) is not { } credentials)
            {
                return null;
            }

            // Another request may have refreshed them while this one waited for the lock.
            return IsAboutToExpire(credentials)
                ? await RefreshAsync(source, entry!, beforeUse: true, cancellationToken).ConfigureAwait(false)
                : credentials;
        }
    }

    public async Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rejected);

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            VaultEntry? entry = await _vault.GetAsync(source, cancellationToken).ConfigureAwait(false);
            if (Usable(entry) is not { } current)
            {
                return null;
            }

            // Another request refreshed them already, or the account was linked again: use those.
            return current == rejected
                ? await RefreshAsync(source, entry!, beforeUse: false, cancellationToken).ConfigureAwait(false)
                : current;
        }
    }

    private static AccountCredentials? Usable(VaultEntry? entry) => entry is { NeedsRelink: false } ? entry.Credentials : null;

    /// <summary>
    /// Refreshes the entry's credentials. Call while holding the source's lock.
    /// </summary>
    /// <param name="beforeUse">
    /// Whether the credentials still work for a moment. If so, a refresh that fails for any reason other than the
    /// site refusing it returns them unchanged, so a network hiccup doesn't block the request.
    /// </param>
    private async Task<AccountCredentials?> RefreshAsync(SourceId source, VaultEntry entry, bool beforeUse, CancellationToken cancellationToken)
    {
        IAuthStrategy? strategy = _sources.Find<IAccountLinker>(source)?.Strategies.FirstOrDefault(offered => offered.Kind == entry.Kind);
        if (strategy is null)
        {
            LogNoStrategy(_logger, source, entry.Kind);
            return beforeUse ? entry.Credentials : null;
        }

        AccountCredentials? refreshed;
        try
        {
            refreshed = await strategy.RefreshAsync(entry.Credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationFailedException)
        {
            refreshed = null;
        }
        catch (Exception exception) when (beforeUse && exception is not OperationCanceledException)
        {
            LogRefreshFailed(_logger, exception, source);
            return entry.Credentials;
        }

        if (refreshed is null)
        {
            _vault.MarkNeedsRelink(source);
            LogNeedsRelink(_logger, source);
            return null;
        }

        await _vault.SaveAsync(source, entry with { Credentials = refreshed }, CancellationToken.None).ConfigureAwait(false);
        LogRefreshed(_logger, source);
        return refreshed;
    }

    private bool IsAboutToExpire(AccountCredentials credentials)
        => credentials.ExpiresAt is { } expiresAt && expiresAt - _timeProvider.GetUtcNow() <= RefreshBeforeExpiry;
}
