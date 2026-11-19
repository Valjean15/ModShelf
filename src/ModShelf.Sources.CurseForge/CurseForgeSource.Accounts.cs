using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Resources;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="IAccountLinker"/> half: the user's API key, which CurseForge checks when asked for a game. A key
/// doesn't say whose it is, so the account is called after it.
/// </summary>
public sealed partial class CurseForgeSource : IAccountLinker
{
    /// <summary>
    /// False: CurseForge answers no request without an API key.
    /// </summary>
    public bool WorksWithoutAccount => false;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    /// <exception cref="ArgumentException">The kind isn't <see cref="AuthKind.ApiKey"/>, the only way CurseForge links.</exception>
    /// <exception cref="AuthenticationFailedException">CurseForge refused the key.</exception>
    /// <exception cref="HttpRequestException">CurseForge couldn't be reached, or refused for another reason.</exception>
    public async Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        IAuthStrategy strategy = Strategies.FirstOrDefault(offered => offered.Kind == kind)
            ?? throw new ArgumentException($"CurseForge doesn't link accounts with {kind}.", nameof(kind));

        HttpRequestMessage check = new(HttpMethod.Get, _api.Address("v1/games?index=0&pageSize=1"));
        strategy.Authorize(check, credentials);
        _ = await _api.SendAsync<IReadOnlyList<CurseForgeGame>>(check, cancellationToken).ConfigureAwait(false);
        return new AccountProfile(Strings.KeyAccount, tier: Strings.TierKey);
    }
}
