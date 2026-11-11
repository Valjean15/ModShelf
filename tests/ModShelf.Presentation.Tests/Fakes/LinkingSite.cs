using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A site that links accounts, as Nexus Mods: with none to link with. It's never asked anything.
/// </summary>
internal sealed class LinkingSite(SourceId id, string displayName) : IAccountLinker
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public SiteWords Words => SiteWords.None;

    public bool WorksWithoutAccount => false;

    public IReadOnlyList<IAuthStrategy> Strategies => [];

    public Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
}
