using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Installed;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Common;

/// <summary>
/// The games installed on this computer, each with its games on the sites, for <c>mod games --installed</c> and the MCP
/// server's <c>mod_games</c> with <c>installed</c>.
/// </summary>
internal static class InstalledGameList
{
    /// <summary>
    /// Reads the games of the sites that list them, <paramref name="source"/>'s alone when given, then matches the
    /// installed games with them. Kept are the installed games whose names, or their games' names, have
    /// <paramref name="query"/>'s words; with <paramref name="source"/>, each with its games there alone. A site whose
    /// games can't be read is passed over, its games then matching only as chosen.
    /// </summary>
    public static async Task<InstalledGameListing> ReadAsync(
        IModLookup lookup,
        ISourceRegistry sources,
        IInstalledGameMatcher matcher,
        SourceId? source,
        NameQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(query);

        // Every site asked at once, its problem kept in the sites' order.
        IModCatalog[] catalogs = [.. sources.All<IModCatalog>().Where(catalog => (source is null || catalog.Id == source) && !lookup.FindsGamesByName(catalog.Id))];
        (IReadOnlyList<GameInfo> Games, UnreadSite? Unread)[] read = await Task.WhenAll(catalogs.Select(catalog => SiteGamesAsync(lookup, catalog, cancellationToken))).ConfigureAwait(false);
        GameInfo[] games = [.. read.SelectMany(site => site.Games)];
        UnreadSite[] unread = [.. read.Select(site => site.Unread).OfType<UnreadSite>()];
        InstalledGamesMatched matched = await matcher.MatchAsync(games, cancellationToken).ConfigureAwait(false);
        InstalledGameMatch[] shown =
        [
            .. matched.Games
                .Select(match => source is null ? match : match with { Sites = [.. match.Sites.Where(site => site.Site == source)] })
                .Where(match => query.IsEmpty || query.Matches(match.Game.Name) || match.Sites.Any(site => site.Games.Any(game => query.Matches(game.Name)))),
        ];
        return new InstalledGameListing(matched.Stores, shown, unread);
    }

    private static async Task<(IReadOnlyList<GameInfo> Games, UnreadSite? Unread)> SiteGamesAsync(IModLookup lookup, IModCatalog catalog, CancellationToken cancellationToken)
    {
        try
        {
            return (await lookup.GetGamesAsync(catalog.Id, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ([], new UnreadSite(catalog.Id, catalog.DisplayName, exception));
        }
    }
}
