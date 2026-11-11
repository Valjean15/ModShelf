using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A site ModShelf uses with no account that lists its updates and resolves downloads, but can't be searched. It's
/// never asked anything.
/// </summary>
internal sealed class FeedSite(SourceId id, string displayName) : IDownloadResolver, IUpdateFeed
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public SiteWords Words => SiteWords.None;

    public bool HandsOverLinks => true;

    public TimeSpan Reach => TimeSpan.FromDays(28);

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken) => throw new NotSupportedException();
}
