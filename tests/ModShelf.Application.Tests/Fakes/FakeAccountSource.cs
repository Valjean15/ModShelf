using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A source with accounts, and a quota the test sets. While <see cref="RefusesProfiles"/> is set, it refuses every token
/// when asked for the profile.
/// </summary>
internal sealed class FakeAccountSource(SourceId id, params FakeStrategy[] strategies) : IAccountLinker, IApiQuota
{
    public SourceId Id { get; } = id;

    public string DisplayName => $"{Id.Value} site";

    public bool WorksWithoutAccount { get; init; }

    public IReadOnlyList<IAuthStrategy> Strategies { get; } = strategies;

    public bool RefusesProfiles { get; set; }

    /// <summary>
    /// The account's tier, as the site gives it from now on.
    /// </summary>
    public string Tier { get; set; } = "Premium";

    public ApiQuota? Quota { get; set; }

    /// <summary>
    /// The access token of each profile lookup, oldest first.
    /// </summary>
    public List<string> ProfileTokens { get; } = [];

    /// <summary>
    /// Runs while <see cref="GetProfileAsync"/> asks the site, before it answers.
    /// </summary>
    public Func<Task>? WhileLookingUpProfile { get; set; }

    /// <summary>
    /// The kind each profile lookup was told the credentials came from, oldest first.
    /// </summary>
    public List<AuthKind> ProfileKinds { get; } = [];

    public async Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
    {
        ProfileKinds.Add(kind);
        ProfileTokens.Add(credentials.AccessToken);
        if (WhileLookingUpProfile is not null)
        {
            await WhileLookingUpProfile();
        }

        return RefusesProfiles
            ? throw new AuthenticationFailedException("The site refused the token.")
            : new AccountProfile("Jean", new Uri("https://avatars.example.com/jean.png"), Tier);
    }
}
