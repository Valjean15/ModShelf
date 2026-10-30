using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Names the sites the screens talk about.
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
}
