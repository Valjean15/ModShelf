using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Common;

/// <summary>
/// Which sites could have a game named by its key alone, such as <c>fallout4</c>: those whose games' keys look like it
/// (<see cref="IModCatalog.CouldBeGameKey"/>), as GameBanana's are numbers.
/// </summary>
internal static class GameSites
{
    /// <summary>
    /// The sites of <paramref name="sites"/> whose games could be named <paramref name="key"/>; all of them when none could,
    /// so whoever asks can say which there are.
    /// </summary>
    public static IReadOnlyList<SourceId> ThatCouldHave(string key, IReadOnlyList<SourceId> sites, ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(sources);

        SourceId[] could = [.. sites.Where(site => sources.Find<IModCatalog>(site)?.CouldBeGameKey(key.Trim()) != false)];
        return could.Length > 0 ? could : sites;
    }
}
