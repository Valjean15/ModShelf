using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A site ModShelf uses with no account, as DEG Mods: it searches, resolves downloads and lists updates; none is asked.
/// </summary>
internal sealed class OpenSite(SourceId id, string displayName) : IModSearch, IDownloadResolver, IUpdateFeed
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public SiteWords Words => SiteWords.None;

    public SearchCapabilities Capabilities { get; } = new([new(SearchSort.Newest, SearchDirection.Descending)], SearchFilters.None, 20);

    public bool HandsOverLinks => false;

    public TimeSpan Reach => TimeSpan.FromDays(28);

    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken) => throw new NotSupportedException();
}
