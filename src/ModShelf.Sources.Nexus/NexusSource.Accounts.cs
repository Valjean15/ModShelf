using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Resources;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IAccountLinker"/> half: the ways to link an account, and whose account credentials belong to.
/// </summary>
public sealed partial class NexusSource
{
    private const string ValidatePath = "v1/users/validate.json";

    /// <summary>
    /// False: every request to the API needs an account, and so does every download.
    /// </summary>
    public bool WorksWithoutAccount => false;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    /// <summary>
    /// Asks the API whose account the credentials belong to, sent the way <paramref name="kind"/> sends them.
    /// </summary>
    /// <exception cref="ArgumentException">The source doesn't offer <paramref name="kind"/>.</exception>
    /// <exception cref="AuthenticationFailedException">The API refused the credentials.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused for another reason.</exception>
    public async Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        IAuthStrategy strategy = Strategies.FirstOrDefault(offered => offered.Kind == kind)
            ?? throw new ArgumentException($"Nexus Mods doesn't link accounts with {kind}.", nameof(kind));

        HttpRequestMessage request = new(HttpMethod.Get, new Uri(ValidatePath, UriKind.Relative));
        strategy.Authorize(request, credentials);
        NexusUser user = await _api.SendAsync<NexusUser>(request, cancellationToken).ConfigureAwait(false);
        return new AccountProfile(user.Name, WebLink(user.ProfileUrl), Tier(user));
    }

    private static string Tier(NexusUser user) => user switch
    {
        { IsPremium: true } => Strings.TierPremium,
        { IsSupporter: true } => Strings.TierSupporter,
        _ => Strings.TierFree,
    };
}
