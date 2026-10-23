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

    public Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, CancellationToken cancellationToken)
        => RefusesProfiles
            ? throw new AuthenticationFailedException("The site refused the token.")
            : Task.FromResult(new AccountProfile("Jean", new Uri("https://avatars.example.com/jean.png"), "Premium"));
}
