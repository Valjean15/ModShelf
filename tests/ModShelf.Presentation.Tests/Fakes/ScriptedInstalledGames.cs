using ModShelf.Application.Common;
using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The games installed on this computer as the test sets them, Steam's, matched as ModShelf matches them: by the game
/// picked on a site, or else by the same name among the games listed. Picks are kept in memory, and the test can make
/// matching or keeping a pick fail.
/// </summary>
internal sealed class ScriptedInstalledGames : IInstalledGameMatcher
{
    public static readonly GameStore Steam = new("steam", "Steam", Path.Combine(Path.GetTempPath(), "Steam"));

    /// <summary>
    /// Whether Steam is on this computer; with it not, there are no games.
    /// </summary>
    public bool HasSteam { get; set; } = true;

    public List<InstalledGame> Games { get; } = [];

    /// <summary>
    /// The games picked: by the installed game's key, then by site, the game picked, or null for none.
    /// </summary>
    public Dictionary<string, Dictionary<SourceId, GameInfo?>> Picks { get; } = [];

    /// <summary>
    /// The games each matching was given, in turn.
    /// </summary>
    public List<IReadOnlyList<GameInfo>> Matched { get; } = [];

    /// <summary>
    /// What matching throws instead, while set.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// What keeping a pick throws instead, while set.
    /// </summary>
    public Exception? PickFailure { get; set; }

    public InstalledGame Add(string id, string name)
    {
        InstalledGame game = new("steam", id, name, Path.Combine(Steam.Folder, "steamapps", "common", name));
        Games.Add(game);
        return game;
    }

    /// <summary>
    /// Picks a game the matching shows as it is, as a game of a site that finds its games by name; null for none.
    /// </summary>
    public void Pick(InstalledGame game, SourceId site, GameInfo? picked)
    {
        if (!Picks.TryGetValue(game.Key, out Dictionary<SourceId, GameInfo?>? sites))
        {
            Picks[game.Key] = sites = [];
        }

        sites[site] = picked;
    }

    public Task<InstalledGamesMatched> MatchAsync(IEnumerable<GameInfo> listed, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromException<InstalledGamesMatched>(failure);
        }

        GameInfo[] games = [.. listed.DistinctBy(game => game.Game)];
        Matched.Add(games);
        SourceId[] sites = [.. games.Select(game => game.Game.Source).Distinct()];
        List<InstalledGameMatch> matched = [];
        foreach (InstalledGame game in HasSteam ? Games : [])
        {
            Dictionary<SourceId, GameInfo?> picked = Picks.GetValueOrDefault(game.Key) ?? [];
            matched.Add(new InstalledGameMatch(
                game,
                [
                    .. sites.Concat(picked.Keys.Where(site => !sites.Contains(site))).Select(site => picked.TryGetValue(site, out GameInfo? info)
                        ? new SiteMatch(site, info is null ? [] : [info], Picked: true)
                        : new SiteMatch(site, [.. games.Where(listing => listing.Game.Source == site && NameQuery.SameName(listing.Name, game.Name))], Picked: false)),
                ]));
        }

        return Task.FromResult(new InstalledGamesMatched(HasSteam ? [Steam] : [], matched));
    }

    public Task PickAsync(InstalledGame game, SourceId site, GameRef? picked, CancellationToken cancellationToken)
    {
        if (PickFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        Pick(game, site, picked is null ? null : new GameInfo(picked, Matched.LastOrDefault()?.FirstOrDefault(listed => listed.Game == picked)?.Name ?? picked.Key));
        return Task.CompletedTask;
    }

    public Task UnpickAsync(InstalledGame game, SourceId site, CancellationToken cancellationToken)
    {
        if (PickFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        if (Picks.TryGetValue(game.Key, out Dictionary<SourceId, GameInfo?>? sites))
        {
            sites.Remove(site);
        }

        return Task.CompletedTask;
    }
}
