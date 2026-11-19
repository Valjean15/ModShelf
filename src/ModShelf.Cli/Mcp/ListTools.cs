using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Downloads;
using ModShelf.Application.Lists;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools for lists of mods (ADR 0098): the library exported as a list, and a list, or a site's collection,
/// imported into it. An import's downloads go to whatever runs the queue, as the download tool's do
/// (<see cref="McpQueue"/>).
/// </summary>
[McpServerToolType]
internal sealed class ListTools(IModLists lists, McpQueue queue)
{
    private const string DesktopApp = "desktop app";
    private const string ThisServer = "this MCP server";

    [McpServerTool(Name = "library_export", Title = "Export the library as a list", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Exports ModShelf's library as a list of its mods and their files, by the sites' IDs, in ModShelf's list format (modshelf-list, JSON): each mod's site, game, ID, name, page, whether it's watched or pinned, its note, and its files but the old versions archived. Save the list as a file to back the library up or share it; library_import, or the desktop app's Import list, brings it into a library. Nothing is asked of any site.")]
    public Task<ListExport> ExportAsync(
        [Description("Only this game's mods, as the site names it in its links, such as stardewvalley.")] string? game = null,
        [Description("Only this site's mods: nexus, gamebanana, modio, degmods, thunderstore or curseforge.")] string? source = null,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            ModList all = await lists.ExportAsync(ListScope.Library, cancellationToken).ConfigureAwait(false);
            ModList list = all with
            {
                Mods =
                [
                    .. all.Mods
                        .Where(mod => game is null || string.Equals(mod.Mod.Game.Key, game.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Where(mod => source is null || string.Equals(mod.Mod.Game.Source.Value, source.Trim(), StringComparison.OrdinalIgnoreCase)),
                ],
            };
            int files = list.Mods.Sum(mod => mod.Files.Count);
            return new ListExport(
                lists.Write(list),
                list.Mods.Count,
                files,
                string.Create(CultureInfo.InvariantCulture, $"The list holds {list.Mods.Count} mods and {files} files."));
        });

    [McpServerTool(Name = "library_import", Title = "Import a list or a collection into the library", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Brings a list of mods into ModShelf's library: a list in ModShelf's list format (as library_export gives it), or a collection on its site, by its link, such as a Nexus Mods collection's page. The mods new to the library are watched, with a list's pins and notes, and the listed files the library lacks are downloaded into it, as the download tool downloads them; a collection's files at the versions it pins. A mod the library has keeps its own choices. With preview, nothing is done: the counts say what importing would do, which is worth showing the user first for a long list. Nothing is installed: a collection's install steps are never run, and what it needs from elsewhere is only named, in elsewhere, for the user to get. With a free Nexus Mods account, the user starts each download on the site: those downloads wait, with the page to open, as downloads_list shows.")]
    public Task<ListImportEntry> ImportAsync(
        [Description("The list: its JSON text, in the modshelf-list format. Give this or collection.")] string? list = null,
        [Description("A collection's link, such as https://www.nexusmods.com/games/stardewvalley/collections/abcdef; with /revisions/<n> for that revision, otherwise the latest. Give this or list.")] string? collection = null,
        [Description("Only say what importing would do.")] bool preview = false,
        [Description("Only watch the list's mods; download nothing.")] bool watchOnly = false,
        [Description("Also download the files the list or collection marks optional.")] bool optional = false,
        [Description("For a listed file its site no longer offers, download the mod's newest main file instead.")] bool newestForMissing = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            ModList read = await ReadAsync(list, collection, cancellationToken).ConfigureAwait(false);

            ListImportOptions options = new(!watchOnly, optional, newestForMissing);
            ListImportPlan plan = await lists.PlanAsync(read, cancellationToken).ConfigureAwait(false);
            int toDownload = options.Download ? plan.FilesToDownload(optional) : 0;
            int optionalLeftOut = options.Download && !optional ? plan.OptionalFiles : 0;
            if (preview)
            {
                return Entry(plan, toDownload, optionalLeftOut, null, null, Previewed(plan, options, toDownload, optionalLeftOut));
            }

            bool ranHere = false;
            ListImportResult result = await lists.ImportAsync(
                    plan,
                    options,
                    null,
                    async (link, token) =>
                    {
                        (QueueReply reply, bool here) = await queue.SendAsync(QueueRequest.Download(link), token).ConfigureAwait(false);
                        ranHere |= here;
                        return reply.Outcome == QueueOutcome.Done
                            ? !reply.AlreadySaved
                            : throw new InvalidOperationException(reply.Problem ?? $"The download wasn't queued: {reply.Outcome}.");
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            string? runBy = result.FilesQueued == 0 ? null : ranHere ? ThisServer : DesktopApp;
            return Entry(plan, toDownload, optionalLeftOut, result, runBy, Imported(result, runBy));
        });

    private static ListElsewhereEntry Elsewhere(ListedElsewhere thing) => new(thing.Name, thing.PageUrl?.AbsoluteUri, thing.Optional);

    private static ListImportEntry Entry(
        ListImportPlan plan,
        int toDownload,
        int optionalLeftOut,
        ListImportResult? result,
        string? runBy,
        string message)
        => new(
            result is null,
            plan.List.Title,
            plan.Mods.Count,
            plan.NewMods,
            plan.ModsInLibrary,
            plan.ModsWithoutSite,
            toDownload,
            optionalLeftOut,
            result?.ModsAdded ?? 0,
            result?.FilesQueued ?? 0,
            result?.FilesInLibrary ?? plan.FilesInLibrary,
            runBy,
            [.. (result?.Problems ?? []).Select(Problem)],
            [.. plan.List.Elsewhere.Select(Elsewhere)],
            plan.List.FilesUnavailable,
            message);

    private static ListProblemEntry Problem(ListImportProblem problem)
    {
        ModRef mod = problem.Mod;
        string said = problem.Kind switch
        {
            ListImportProblemKind.NoSite => $"ModShelf can't reach {mod.Game.Source.Value} here, so the mod is left out.",
            ListImportProblemKind.SiteFailed => $"Its site couldn't be asked about it. {problem.Detail}".TrimEnd(),
            ListImportProblemKind.FileGone => "Its site no longer offers this file; newestForMissing downloads the mod's newest main file instead.",
            _ => problem.File is null ? $"It couldn't be watched. {problem.Detail}".TrimEnd() : $"The file couldn't be queued. {problem.Detail}".TrimEnd(),
        };
        return new ListProblemEntry(
            mod.Game.Source.Value,
            mod.Game.Key,
            mod.Id,
            problem.ModName,
            problem.File?.Id,
            JsonNamingPolicy.CamelCase.ConvertName(problem.Kind.ToString()),
            said);
    }

    private static string Previewed(ListImportPlan plan, ListImportOptions options, int toDownload, int optionalLeftOut)
    {
        string mods = string.Create(
            CultureInfo.InvariantCulture,
            $"The list has {plan.Mods.Count} mods: {plan.NewMods} new to the library, {plan.ModsInLibrary} in it already, {plan.ModsWithoutSite} of sites ModShelf can't reach here.");
        if (!options.Download)
        {
            return $"{mods} Importing would watch the new mods, and download nothing.{Collected(plan.List)}";
        }

        string files = string.Create(CultureInfo.InvariantCulture, $" Importing would watch the new mods the list watches, and download {toDownload} files into the library.");
        string optional = optionalLeftOut > 0
            ? string.Create(CultureInfo.InvariantCulture, $" {optionalLeftOut} optional files are left out; optional adds them.")
            : string.Empty;
        return mods + files + optional + Collected(plan.List);
    }

    /// <summary>
    /// What a collection names that isn't downloaded: the files its site no longer offers, and what it needs from
    /// elsewhere; nothing for a list file.
    /// </summary>
    private static string Collected(ModList list)
    {
        string unavailable = list.FilesUnavailable > 0
            ? string.Create(CultureInfo.InvariantCulture, $" {list.FilesUnavailable} files it names are no longer offered by its site.")
            : string.Empty;
        string elsewhere = list.Elsewhere.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $" It needs {list.Elsewhere.Count} things from elsewhere, which ModShelf doesn't download: elsewhere names them, for the user to get.")
            : string.Empty;
        return unavailable + elsewhere;
    }

    private static string Imported(ListImportResult result, string? runBy)
    {
        string done = string.Create(
            CultureInfo.InvariantCulture,
            $"Watched {result.ModsAdded} new mods, queued {result.FilesQueued} files for the library, and found {result.FilesInLibrary} there already.");
        string queued = runBy switch
        {
            ThisServer => " No desktop app is running, so this MCP server downloads them, until no download is under way; downloads_list shows how they go, and the pages to open for those waiting for the user.",
            DesktopApp => " The desktop app downloads them; downloads_list shows how they go, and the pages to open for those waiting for the user.",
            _ => string.Empty,
        };
        string problems = result.Problems.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" {result.Problems.Count} things couldn't be done: problems says which and why.");
        return done + queued + problems;
    }

    /// <summary>
    /// The list to import: the list's text read, or the collection the link is a page of read on its site.
    /// </summary>
    /// <exception cref="McpException">Neither or both are given, or the text or the link can't be read as one.</exception>
    private async Task<ModList> ReadAsync(string? list, string? collection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(list) == string.IsNullOrWhiteSpace(collection))
        {
            throw new McpException("Give one of list, a list's JSON text as library_export gives it, and collection, a collection's link.");
        }

        if (!string.IsNullOrWhiteSpace(list))
        {
            try
            {
                return lists.Read(list);
            }
            catch (InvalidDataException exception)
            {
                throw new McpException(exception.Message, exception);
            }
        }

        try
        {
            return await lists.ReadCollectionAsync(McpProblems.Link(collection!), cancellationToken).ConfigureAwait(false)
                ?? throw new McpException("That link isn't to a collection ModShelf can read, such as a Nexus Mods collection's page.");
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new McpException("The site has no such collection.", exception);
        }
    }
}
