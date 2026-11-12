using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Installed;

/// <summary>
/// An installed game, and its games on each site matched.
/// </summary>
public sealed record InstalledGameMatch(InstalledGame Game, IReadOnlyList<SiteMatch> Sites)
{
    /// <summary>
    /// Its match on <paramref name="site"/>; null when the site wasn't matched.
    /// </summary>
    public SiteMatch? On(SourceId site) => Sites.FirstOrDefault(match => match.Site == site);
}
