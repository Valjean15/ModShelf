using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Installed;

/// <summary>
/// The games installed on this computer, each with its game on each site: the one picked for it there, or else the
/// site's games of the same name. ModShelf never picks for the user: a name two games of a site share matches both.
/// </summary>
public interface IInstalledGameMatcher
{
    /// <summary>
    /// Reads the installed games, then matches each on each site whose games <paramref name="listed"/> holds, and each
    /// site a game was picked on: the game picked for it there, or none when none was; or else the site's games in
    /// <paramref name="listed"/> whose names are the same (<see cref="Common.NameQuery.SameName"/>), none, one or several.
    /// A game picked that <paramref name="listed"/> doesn't hold, as on a site that finds its games by name, is asked of
    /// its site; when the site can't say, its key stands for its name.
    /// </summary>
    /// <param name="listed">The sites' games the caller read, as their lists of games give them.</param>
    Task<InstalledGamesMatched> MatchAsync(IEnumerable<GameInfo> listed, CancellationToken cancellationToken);

    /// <summary>
    /// Remembers <paramref name="picked"/> as <paramref name="game"/>'s game on <paramref name="site"/>, in the
    /// settings; null: it has none there.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="picked"/> is another site's game.</exception>
    Task PickAsync(InstalledGame game, SourceId site, GameRef? picked, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets what was picked for <paramref name="game"/> on <paramref name="site"/>: its games there are those of the
    /// same name again.
    /// </summary>
    Task UnpickAsync(InstalledGame game, SourceId site, CancellationToken cancellationToken);
}
