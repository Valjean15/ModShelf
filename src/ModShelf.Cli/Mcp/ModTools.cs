using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools that look mods up on their sites: a mod's details and files, searching a game's mods, and finding a
/// site's games by name. Nothing is downloaded or saved.
/// </summary>
[McpServerToolType]
internal sealed class ModTools(IModLookup lookup, IModLibrary library, ISettingsService settings, ISourceRegistry sources, TimeProvider timeProvider)
{
    /// <summary>
    /// How many mods a page lists unless pageSize says otherwise, or fewer when the site's pages hold fewer.
    /// </summary>
    private const int DefaultPageSize = 20;

    /// <summary>
    /// The most games a page of <c>mod_games</c> lists.
    /// </summary>
    private const int MostGamesAPage = 100;

    // The sections listed unless all files are asked for, as the sites show them.
    private static readonly ModFileCategory[] CurrentSections =
        [ModFileCategory.Main, ModFileCategory.Update, ModFileCategory.Optional, ModFileCategory.Miscellaneous];

    // The filters a search can take, by the argument that takes each.
    private static readonly (SearchFilters Filter, string Argument)[] FilterArguments =
    [
        (SearchFilters.Text, "words"),
        (SearchFilters.Author, "author"),
        (SearchFilters.Category, "category"),
        (SearchFilters.UpdatedSince, "updatedWithin"),
        (SearchFilters.AdultContent, "adult"),
    ];

    // Each game's categories, read once while the server runs: every page of a search says them.
    private readonly ConcurrentDictionary<GameRef, GameInfo> _games = new();

    [McpServerTool(Name = "mod_show", Title = "Show a mod", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Shows a mod as its site describes it, with its files, each with the link to download it, the newer file that replaces it by the site's word, where the library has it, if it does, and whether it's an update skipped; and whether the library has the mod pinned, and its note. Old versions, archived files and files in no section are left out unless allFiles is set, but for the one the link points to. The library's files that the site no longer lists, such as a pulled file, are in libraryOnly; newerInLibrary says when one of them is newer than everything the site lists.")]
    public Task<ModDetails> ShowAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        [Description("Also list old versions, archived files and files in no section.")] bool allFiles = false,
        [Description("Also give the mod's changelog.")] bool changelog = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            ModPage page = await lookup.LookUpAsync(McpProblems.Link(link), cancellationToken).ConfigureAwait(false);
            LibraryMod? known = await library.FindAsync(page.Mod.Mod, cancellationToken).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);

            ModFile[] listed =
            [
                .. page.Files
                    .Where(file => allFiles || CurrentSections.Contains(file.Category) || file.Reference == page.LinkedFile)
                    .OrderBy(file => Array.IndexOf(CurrentSections, file.Category) is var section and >= 0 ? section : CurrentSections.Length)
                    .ThenByDescending(file => file.UploadedAt),
            ];
            ModInfo mod = page.Mod;
            HashSet<ModFileRef> onSite = [.. page.Files.Select(file => file.Reference)];
            LibraryFileEntry[] libraryOnly =
            [
                .. (known?.Files ?? [])
                    .Where(file => !onSite.Contains(file.File))
                    .OrderByDescending(file => file.UploadedAt)
                    .Select(file => McpEntries.File(file, roots)),
            ];
            DateTimeOffset? newestOnSite = page.Files.Count == 0 ? null : page.Files.Max(file => file.UploadedAt);
            return new ModDetails(
                mod.Mod.Game.Source.Value,
                mod.Mod.Game.Key,
                page.Game.Name,
                mod.Mod.Id,
                mod.Name,
                mod.Author,
                mod.Version,
                mod.Summary,
                mod.PageUrl?.AbsoluteUri,
                mod.UpdatedAt,
                mod.HasAdultContent,
                mod.IsAvailable,
                mod.Downloads,
                mod.Endorsements,
                known?.IsWatched ?? false,
                known?.IsPinned ?? false,
                known?.Note,
                [
                    .. listed.Select(file =>
                    {
                        LibraryFile? have = known?.Files.FirstOrDefault(kept => kept.File == file.Reference);
                        string? path = have is null ? null : roots.Resolve(have);
                        return new ModFileEntry(
                            file.Reference.Id,
                            file.Name,
                            file.VersionLabel,
                            file.Category.ToString(),
                            file.FileName,
                            file.SizeInBytes,
                            file.UploadedAt,
                            file.PageUrl?.AbsoluteUri,
                            file.ReplacedBy?.Id,
                            file.ReplacedBy is { } next ? onSite.Contains(next) : null,
                            file.Reference == page.LinkedFile,
                            path,
                            have?.Sha256,
                            path is not null && !File.Exists(path),
                            known?.SkippedUpdates.Contains(file.Reference) ?? false);
                    }),
                ],
                page.Files.Count - listed.Length,
                libraryOnly,
                libraryOnly.Any(file => newestOnSite is null || file.UploadedAt > newestOnSite),
                changelog ? [.. page.Changelogs.Select(entry => new ChangelogEntry(entry.Version, entry.Changes))] : null);
        });

    [McpServerTool(Name = "mod_search", Title = "Search a game's mods", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists a game's mods on its site that match, a page at a time, each with the link mod_show and download take. Mods with adult content are left out unless asked for; leftOut counts those a page left out once the site answered. A site searches only by what it can, in its own orders: one asked more is refused, saying what it can. Each result's search says what the site can do for the game: its orders, the filters it takes, its largest page, its word for approval, and the game's categories. endorsements counts whatever the site counts as approval.")]
    public Task<SearchResult> SearchAsync(
        [Description("The game, as its site names it: on Nexus Mods by the name in its links, such as stardewvalley; on GameBanana and mod.io by its number, such as 16951 or 6715. mod_games finds a site's games and their keys.")] string game,
        [Description("Words to look for in the mods' names; none to list every mod of the game.")] string? words = null,
        [Description("Only mods by this author, where the site can search by author.")] string? author = null,
        [Description("Only mods in this category of the game, by its name in any case, where the site can search by category; a result's search.categories lists them. One the game doesn't have is refused, naming those it has.")] string? category = null,
        [Description("Only mods updated within this many days, from 1, such as 7, where the site can search by when mods were updated. The days count from the start of the hour, so a search's pages asked within the hour find the same mods.")] int? updatedWithin = null,
        [Description("Include mods with adult content. A site without adult content has none to include.")] bool adult = false,
        [Description("The order, as far as the site has it: Relevance, Endorsements (whatever the site counts as approval), Downloads, UniqueDownloads, Updated, Newest, Size or Name. The site's own unless set: Relevance on Nexus Mods, which without words is the site's own order. The most comes first; by Name, Z first.")] string? sort = null,
        [Description("Turn the order around, so the least comes first.")] bool ascending = false,
        [Description("Which page of results, from 1.")] int page = 1,
        [Description("How many mods a page lists, from 1 to 100, as many as the site's pages hold at most; 20 unless set, or fewer when the site's pages hold fewer.")] int? pageSize = null,
        [Description("The site to search: nexus, gamebanana or modio. Needed only when the game's key could be more than one site's; each result says its site as source.")] string? source = null,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            SearchSort? order = null;
            if (sort is not null)
            {
                if (!Enum.TryParse(sort, ignoreCase: true, out SearchSort named) || !Enum.GetNames<SearchSort>().Contains(sort.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new McpException($"sort must be one of {string.Join(", ", Enum.GetNames<SearchSort>())}.");
                }

                order = named;
            }

            if (pageSize is < 1 or > SearchQuery.MaxCount)
            {
                throw new McpException($"pageSize must be from 1 to {SearchQuery.MaxCount}.");
            }

            if (updatedWithin is < 1 or > SearchArguments.MostDays)
            {
                throw new McpException($"updatedWithin must be from 1 to {SearchArguments.MostDays} days.");
            }

            // Without a site named, those whose games' keys look like the game's could have it.
            SourceId site = ChooseSource(
                source is null ? GameSites.ThatCouldHave(game, lookup.SearchableSources, sources) : lookup.SearchableSources,
                source,
                "search");
            GameRef searched = new(site, game.Trim());

            // The site's own order and page size unless asked otherwise; a whole number of pages in, so each page starts
            // where the last ended.
            IModSearch? search = sources.Find<IModSearch>(site);
            SearchCapabilities? capabilities = search?.Capabilities;
            SearchOrder own = capabilities?.Orders[0] ?? new SearchOrder(SearchSort.Relevance, SearchDirection.Descending);
            int count = pageSize ?? Math.Min(DefaultPageSize, capabilities?.MaxCount ?? DefaultPageSize);
            if (page < 1 || (long)(page - 1) * count > int.MaxValue)
            {
                throw new McpException("page must be 1 or more, and not that many pages in.");
            }

            SearchDirection direction = order is null ? own.Direction : SearchDirection.Descending;
            if (ascending)
            {
                direction = direction == SearchDirection.Ascending ? SearchDirection.Descending : SearchDirection.Ascending;
            }

            // A category is the game's, as the site names it. A site that can't search by category refuses it, as
            // Application does.
            if (category is not null && capabilities?.Has(SearchFilters.Category) == true && await GameAsync(searched, cancellationToken).ConfigureAwait(false) is { } described)
            {
                category = SearchArguments.Category(category, described, search!.DisplayName);
            }

            DateTimeOffset? since = updatedWithin is { } days ? SearchArguments.UpdatedSince(timeProvider.GetUtcNow(), days) : null;
            SearchQuery query = new(words ?? string.Empty, order ?? own.Sort, (page - 1) * count, count, author, adult, direction, category, since);
            SearchPage found = await lookup.SearchAsync(searched, query, cancellationToken).ConfigureAwait(false);
            return new SearchResult(
                found.TotalCount,
                found.Offset,
                found.HasMore,
                found.LeftOut,
                [
                    .. found.Mods.Select(mod => new SearchEntry(
                        mod.Mod.Game.Source.Value,
                        mod.Mod.Game.Key,
                        mod.Mod.Id,
                        mod.Name,
                        mod.Author,
                        mod.Summary,
                        mod.Version,
                        mod.UpdatedAt,
                        mod.Endorsements,
                        mod.Downloads,
                        mod.HasAdultContent,
                        mod.IsAvailable,
                        mod.PageUrl?.AbsoluteUri)),
                ],
                await SiteSearchAsync(search!, searched, cancellationToken).ConfigureAwait(false));
        });

    [McpServerTool(Name = "mod_games", Title = "Find a site's games", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists a site's games whose names match, those that start with the words first, a page at a time, each with the key mod_search's game takes. Each word must start a word of the name, in any order; case, accents, quotes and other punctuation don't count, and a Roman numeral from II to XXX is its number too: \"stalker 2\" finds S.T.A.L.K.E.R. 2, \"final fantasy 12\" Final Fantasy XII.")]
    public Task<GamesResult> GamesAsync(
        [Description("Words of the game's name; none to list every game, on a site that lists them all. GameBanana finds its games by name only, by a word of 3 letters or more.")] string? words = null,
        [Description("The site: nexus, gamebanana or modio. Needed when more than one site lists its games, as several do: ask each site the game may be on.")] string? source = null,
        [Description("Which page of games, from 1.")] int page = 1,
        [Description("How many games a page lists, from 1 to 100.")] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            if (pageSize is < 1 or > MostGamesAPage)
            {
                throw new McpException($"pageSize must be from 1 to {MostGamesAPage}.");
            }

            if (page < 1 || (long)(page - 1) * pageSize > int.MaxValue)
            {
                throw new McpException("page must be 1 or more, and not that many pages in.");
            }

            SourceId site = ChooseSource([.. sources.All<IModCatalog>().Select(catalog => catalog.Id)], source, "list its games");
            IReadOnlyList<GameInfo> found = await lookup.FindGamesAsync(site, NameQuery.Parse(words), cancellationToken).ConfigureAwait(false);
            int offset = (page - 1) * pageSize;
            GameEntry[] shown =
            [
                .. found.Skip(offset).Take(pageSize).Select(game => new GameEntry(game.Game.Source.Value, game.Game.Key, game.Name, game.PageUrl?.AbsoluteUri)),
            ];
            return new GamesResult(found.Count, offset, offset + shown.Length < found.Count, shown);
        });

    /// <summary>
    /// The game as its site describes it, read the first time it's asked for; null when its site has no catalog.
    /// </summary>
    private async Task<GameInfo?> GameAsync(GameRef game, CancellationToken cancellationToken)
    {
        if (_games.TryGetValue(game, out GameInfo? kept))
        {
            return kept;
        }

        return sources.Find<IModCatalog>(game.Source) is { } catalog
            ? _games.GetOrAdd(game, await catalog.GetGameAsync(game, cancellationToken).ConfigureAwait(false))
            : null;
    }

    /// <summary>
    /// What <paramref name="search"/> can do for <paramref name="game"/>. Categories that can't be read are left out: the
    /// search itself went well.
    /// </summary>
    private async Task<SiteSearch> SiteSearchAsync(IModSearch search, GameRef game, CancellationToken cancellationToken)
    {
        SearchCapabilities capabilities = search.Capabilities;
        IReadOnlyList<string>? categories = null;
        if (capabilities.Has(SearchFilters.Category))
        {
            try
            {
                categories = (await GameAsync(game, cancellationToken).ConfigureAwait(false))?.Categories;
            }
            catch (Exception exception) when (exception is HttpRequestException or AuthenticationFailedException or InvalidDataException or ArgumentException)
            {
                categories = null;
            }
        }

        return new SiteSearch(
            search.DisplayName,
            [
                .. capabilities.Orders.Select(order => order.Sort).Distinct().Select(sort => new SiteSearchOrder(
                    sort.ToString(),
                    capabilities.Has(new SearchOrder(sort, SearchDirection.Descending)),
                    capabilities.Has(new SearchOrder(sort, SearchDirection.Ascending)))),
            ],
            [.. FilterArguments.Where(filter => capabilities.Has(filter.Filter)).Select(filter => filter.Argument)],
            capabilities.MaxCount,
            search.Words.Approvals,
            categories);
    }

    /// <summary>
    /// The site asked for, or the only one that can.
    /// </summary>
    /// <param name="what">What the site must be able to do, such as "search".</param>
    /// <exception cref="McpException">That site can't, or there's no choice to make.</exception>
    private static SourceId ChooseSource(IReadOnlyList<SourceId> able, string? asked, string what)
    {
        if (asked is null)
        {
            return able.Count switch
            {
                1 => able[0],
                0 => throw new McpException($"No site ModShelf knows can {what}."),
                _ => throw new McpException($"More than one site can {what}; choose one with source: {string.Join(", ", able)}."),
            };
        }

        return able.FirstOrDefault(source => string.Equals(source.Value, asked.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"'{asked}' can't {what}. Sites that can: {string.Join(", ", able)}.");
    }
}
