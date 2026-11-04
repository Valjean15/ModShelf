using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Names the sites the screens talk about, and what each calls following a mod and approving one.
/// </summary>
internal static class SiteNames
{
    /// <summary>
    /// The site's name, such as "Nexus Mods"; its ID when no source of that ID is registered.
    /// </summary>
    public static string SiteName(this ISourceRegistry sources, SourceId source)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Find<IModSource>(source)?.DisplayName ?? source.Value;
    }

    /// <summary>
    /// The site's words, such as Nexus Mods' "track" and "endorsements"; none when no source of that ID is registered.
    /// </summary>
    public static SiteWords Words(this ISourceRegistry sources, SourceId source)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Find<IModSource>(source)?.Words ?? SiteWords.None;
    }
}
