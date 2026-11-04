using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A site's search known only by what it can do, for the screens to offer; the tests' searches go through
/// <see cref="ScriptedLookup"/>, so it's never asked one.
/// </summary>
internal sealed class CapabilitiesOnlySearch(SourceId id, SearchCapabilities capabilities) : IModSearch
{
    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public SiteWords Words => SiteWords.None;

    public SearchCapabilities Capabilities { get; } = capabilities;

    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The tests search through the scripted lookup.");
}
