using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions;

/// <summary>
/// A mod site that ModShelf can work with. A source implements only the capability interfaces its
/// site supports, such as <see cref="Downloads.IDownloadResolver"/>.
/// </summary>
public interface IModSource
{
    /// <summary>
    /// The source's stable key, such as <c>nexus</c>.
    /// </summary>
    SourceId Id { get; }

    /// <summary>
    /// The site's name as shown to the user, such as "Nexus Mods".
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// What the site calls following a mod and approving one, such as Nexus Mods' "track" and "endorsements";
    /// <see cref="SiteWords.None"/> for a site with neither.
    /// </summary>
    SiteWords Words { get; }
}
