using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A source with accounts. While <see cref="RefusesProfiles"/> is set, it refuses every token when asked for the profile.
/// </summary>
internal sealed class FakeAccountSource(SourceId id, params FakeStrategy[] strategies) : IAccountLinker
{
    public SourceId Id { get; } = id;

    public string DisplayName => $"{Id.Value} site";

    public bool WorksWithoutAccount { get; init; }

    public IReadOnlyList<IAuthStrategy> Strategies { get; } = strategies;

    public bool RefusesProfiles { get; set; }

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
        if (WhileLookingUpProfile is not null)
        {
            await WhileLookingUpProfile();
        }

        return RefusesProfiles
            ? throw new AuthenticationFailedException("The site refused the token.")
            : new AccountProfile("Jean", new Uri("https://avatars.example.com/jean.png"), "Premium");
    }
}
