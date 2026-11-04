using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools that look mods up on their sites: a mod's details and files, and searching a game's mods. Nothing is
/// downloaded or saved.
/// </summary>
[McpServerToolType]
internal sealed class ModTools(IModLookup lookup, IModLibrary library, ISettingsService settings, ISourceRegistry sources)
{
    /// <summary>
    /// How many mods a page lists unless pageSize says otherwise, or fewer when the site's pages hold fewer.
    /// </summary>
    private const int DefaultPageSize = 20;

    // The sections listed unless all files are asked for, as the sites show them.
    private static readonly ModFileCategory[] CurrentSections =
        [ModFileCategory.Main, ModFileCategory.Update, ModFileCategory.Optional, ModFileCategory.Miscellaneous];

    [McpServerTool(Name = "mod_show", Title = "Show a mod", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Shows a mod as its site describes it, with its files, each with the link to download it, the newer file that replaces it by the site's word, where the library has it, if it does, and whether it's an update skipped; and whether the library has the mod pinned, and its note. Old versions, archived files and files in no section are left out unless allFiles is set, but for the one the link points to. The library's files that the site no longer lists, such as a pulled file, are in libraryOnly; newerInLibrary says when one of them is newer than everything the site lists.")]
    public Task<ModDetails> ShowAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods.")] string link,
        [Description("Also list old versions, archived files and files in no section.")] bool allFiles = false,
        [Description("Also give the mod's changelog.")] bool changelog = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            ModPage page = await lookup.LookUpAsync(McpProblems.Link(link), cancellationToken).ConfigureAwait(false);
            LibraryMod? known = await library.FindAsync(page.Mod.Mod, cancellationToken).ConfigureAwait(false);
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);

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
                    .Select(file => McpEntries.File(file, root)),
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
                        string? path = have is null ? null : LibraryPaths.Resolve(root, have.RelativePath);
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
    [Description("Lists a game's mods on its site that match, a page at a time, each with the link mod_show and download take. Mods with adult content are left out unless asked for. A site searches only by what it can, in its own orders: one asked more is refused, saying what it can. endorsements counts whatever the site counts as approval.")]
    public Task<SearchResult> SearchAsync(
        [Description("The game, as the site names it in its links, such as stardewvalley on Nexus Mods.")] string game,
        [Description("Words to look for in the mods' names; none to list every mod of the game.")] string? words = null,
        [Description("Only mods by this author, where the site can search by author.")] string? author = null,
        [Description("Include mods with adult content. A site without adult content has none to include.")] bool adult = false,
        [Description("The order, as far as the site has it: Relevance, Endorsements (whatever the site counts as approval), Downloads, UniqueDownloads, Updated, Newest, Size or Name. The site's own unless set: Relevance on Nexus Mods, which without words is the site's own order. The most comes first; by Name, Z first.")] string? sort = null,
        [Description("Turn the order around, so the least comes first.")] bool ascending = false,
        [Description("Which page of results, from 1.")] int page = 1,
        [Description("How many mods a page lists, from 1 to 100, as many as the site's pages hold at most; 20 unless set, or fewer when the site's pages hold fewer.")] int? pageSize = null,
        [Description("The site to search, such as nexus; needed only when more than one site can.")] string? source = null,
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

            SourceId site = ChooseSource(lookup.SearchableSources, source);

            // The site's own order and page size unless asked otherwise; a whole number of pages in, so each page starts
            // where the last ended.
            SearchCapabilities? capabilities = sources.Find<IModSearch>(site)?.Capabilities;
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

            SearchQuery query = new(words ?? string.Empty, order ?? own.Sort, (page - 1) * count, count, author, adult, direction);
            SearchPage found = await lookup.SearchAsync(new GameRef(site, game.Trim()), query, cancellationToken).ConfigureAwait(false);
            return new SearchResult(
                found.TotalCount,
                found.Offset,
                found.HasMore,
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
                ]);
        });

    /// <summary>
    /// The site asked for, or the only one that can search.
    /// </summary>
    /// <exception cref="McpException">That site can't, or there's no choice to make.</exception>
    private static SourceId ChooseSource(IReadOnlyList<SourceId> able, string? asked)
    {
        if (asked is null)
        {
            return able.Count switch
            {
                1 => able[0],
                0 => throw new McpException("No site ModShelf knows can search."),
                _ => throw new McpException($"More than one site can search; choose one with source: {string.Join(", ", able)}."),
            };
        }

        return able.FirstOrDefault(source => string.Equals(source.Value, asked.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"'{asked}' can't search. Sites that can: {string.Join(", ", able)}.");
    }
}
