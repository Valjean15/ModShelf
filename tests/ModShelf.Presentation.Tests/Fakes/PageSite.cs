using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A site whose files that wait for the user are on pages the browser downloads them from, as DEG Mods' MediaFire
/// links are: its pages hand no link over. It's never asked to resolve anything.
/// </summary>
internal sealed class PageSite(SourceId id, string displayName) : IDownloadResolver
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public SiteWords Words => SiteWords.None;

    public bool HandsOverLinks => false;

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken) => throw new NotSupportedException();
}
