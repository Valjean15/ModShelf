using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.ModIo.Accounts;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;
using ModShelf.Sources.ModIo.Resources;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IAccountLinker"/> half: the user's API key or access token, and whose a token is. An API key doesn't
/// say whose it is: mod.io tells only a token's owner.
/// </summary>
public sealed partial class ModIoSource : IAccountLinker
{
    /// <summary>
    /// False: mod.io answers no request without an API key or a token.
    /// </summary>
    public bool WorksWithoutAccount => false;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    /// <summary>
    /// Checks the credentials with mod.io: a token by asking whose it is, which names the account; an API key by asking
    /// for a game, as mod.io tells no key's owner, so the account is called after the key.
    /// </summary>
    /// <exception cref="ArgumentException">The kind isn't <see cref="AuthKind.ApiKey"/>, the only way mod.io links.</exception>
    /// <exception cref="AuthenticationFailedException">mod.io refused the credentials.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused for another reason.</exception>
    public async Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        IAuthStrategy strategy = Strategies.FirstOrDefault(offered => offered.Kind == kind)
            ?? throw new ArgumentException($"mod.io doesn't link accounts with {kind}.", nameof(kind));

        if (ModIoAccessStrategy.Of(credentials) == ModIoCredential.ApiKey)
        {
            HttpRequestMessage check = new(HttpMethod.Get, Api("games?_limit=1"));
            strategy.Authorize(check, credentials);
            _ = await _api.SendAsync<ModIoPage<ModIoGame>>(check, cancellationToken).ConfigureAwait(false);
            return new AccountProfile(Strings.KeyAccount, tier: Strings.TierKey);
        }

        HttpRequestMessage request = new(HttpMethod.Get, Api("me"));
        strategy.Authorize(request, credentials);
        ModIoUser user = await _api.SendAsync<ModIoUser>(request, cancellationToken).ConfigureAwait(false);
        return new AccountProfile(
            string.IsNullOrWhiteSpace(user.Username) ? Strings.TokenAccount : user.Username.Trim(),
            ModIoMods.WebLink(user.Avatar?.Thumb100 ?? user.Avatar?.Original),
            Strings.TierToken);
    }
}
