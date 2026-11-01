using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools for the library: its mods and files, what came in lately, the updates, checking for them, and watching
/// mods.
/// </summary>
[McpServerToolType]
internal sealed class LibraryTools(IModLibrary library, ISettingsService settings, IUpdateChecker checker, IModLookup lookup, TimeProvider time)
{
    [McpServerTool(Name = "library_list", Title = "List the library", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the mods in ModShelf's library, the ones watched for updates and the ones with downloaded files, each with its files (their absolute paths, versions, and whether they're old versions archived) and the updates found for it.")]
    public Task<LibraryListing> ListAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            return new LibraryListing(
                root,
                [
                    .. mods
                        .OrderBy(mod => mod.Mod.Game.Source.Value, StringComparer.Ordinal)
                        .ThenBy(mod => mod.Mod.Game.Key, StringComparer.Ordinal)
                        .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(mod => McpEntries.Mod(mod, root)),
                ]);
        });

    [McpServerTool(Name = "library_changes", Title = "What's new in the library", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the files downloaded into the library since a time, the oldest first, each with its mod's other files in the library: the versions it may replace, where they are, and whether they're archived. Ask again from the result's 'until' to see only what's newer. What library.oldVersions does decides whether old versions stay, are archived or are deleted.")]
    public Task<LibraryChanges> ChangesAsync(
        [Description("From when, as an ISO 8601 date and time, such as 2026-11-01T09:00:00Z; one without an offset is UTC.")] string since,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset from))
            {
                throw new McpException("since isn't a date and time, such as 2026-11-01T09:00:00Z.");
            }

            DateTimeOffset until = time.GetUtcNow();
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            AppSettings stored = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            NewLibraryFile[] files =
            [
                .. mods
                    .SelectMany(mod => mod.Files.Where(file => file.DownloadedAt >= from).Select(file => (Mod: mod, File: file)))
                    .OrderBy(found => found.File.DownloadedAt)
                    .Select(found => new NewLibraryFile(
                        found.Mod.Mod.Game.Source.Value,
                        found.Mod.Mod.Game.Key,
                        found.Mod.Mod.Id,
                        found.Mod.Name,
                        found.Mod.PageUrl?.AbsoluteUri,
                        McpEntries.File(found.File, root),
                        [
                            .. found.Mod.Files
                                .Where(file => file.File != found.File.File)
                                .OrderByDescending(file => file.UploadedAt)
                                .Select(file => McpEntries.File(file, root)),
                        ])),
            ];
            return new LibraryChanges(from, until, (stored.OldVersions ?? OldVersionRule.Default).ToString(), files);
        });

    [McpServerTool(Name = "library_updates", Title = "List the updates", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists the updates the last checks found for the watched mods, each with the link to download it and the library file it replaces, by mod name and the newest first. With changelog, also what each update's version changes, from its mod's changelog, which asks each mod's site.")]
    public Task<UpdateListing> UpdatesAsync(
        [Description("Also read each update's changes from its mod's changelog.")] bool changelog = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<ModRef, LibraryMod> byRef = mods.ToDictionary(mod => mod.Mod);
            List<UpdateFound> updates = [];
            foreach (LibraryMod mod in mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (LibraryUpdate update in mod.Updates.OrderByDescending(update => update.UploadedAt))
                {
                    IReadOnlyList<string>? changes = changelog ? await ChangesOfAsync(update, cancellationToken).ConfigureAwait(false) : null;
                    updates.Add(McpEntries.Found(update, byRef, root, changes));
                }
            }

            return new UpdateListing(mods.Where(mod => mod.IsWatched).Min(mod => mod.CheckedAt), updates);
        });

    [McpServerTool(Name = "library_check", Title = "Check for updates", Idempotent = false, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Checks the watched mods for updates now, asking their sites as little as it can, and lists the new updates found. Nothing is downloaded.")]
    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            UpdateCheck check = await checker.CheckAsync(cancellationToken).ConfigureAwait(false);
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<ModRef, LibraryMod> byRef = (await library.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(mod => mod.Mod);
            return new UpdateCheckResult(
                check.Checked,
                check.Requests,
                [.. check.Found.Select(update => McpEntries.Found(update, byRef, root))],
                [.. check.Failures.Select(failure => new UpdateCheckFailureEntry(failure.Game.Source.Value, failure.Game.Key, failure.Mod?.Id, failure.Reason))]);
        });

    [McpServerTool(Name = "library_watch", Title = "Watch a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Watches a mod for updates, adding it to the library. Nothing is downloaded.")]
    public Task<WatchResult> WatchAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            Uri mod = McpProblems.Link(link);
            LibraryMod watched = await library.WatchAsync(mod, cancellationToken).ConfigureAwait(false);
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            return new WatchResult(true, $"Watching {watched.Name} ({Describe(watched.Mod)}).", McpEntries.Mod(watched, root));
        });

    [McpServerTool(Name = "library_unwatch", Title = "Stop watching a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Stops watching a mod for updates. A mod with downloaded files stays in the library, and its files stay.")]
    public Task<WatchResult> UnwatchAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            Uri mod = McpProblems.Link(link);
            LibraryChange change = await library.UnwatchAsync(mod, cancellationToken).ConfigureAwait(false);
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            string message = change switch
            {
                { Before: null } => $"{char.ToUpperInvariant(Describe(change.Mod)[0])}{Describe(change.Mod)[1..]} isn't in the library.",
                { Before.IsWatched: false } => $"{change.Before.Name} isn't watched.",
                { After: null } => $"Stopped watching {change.Before.Name}. It had no downloaded files, so it's out of the library.",
                _ => $"Stopped watching {change.Before.Name}. Its downloaded files stay in the library.",
            };
            return new WatchResult(false, message, change.After is { } after ? McpEntries.Mod(after, root) : null);
        });

    private static string Describe(ModRef mod) => $"mod {mod.Id} of {mod.Game.Key} on {mod.Game.Source}";

    /// <summary>
    /// What the mod's changelog says of the update's version; null when it can't be read, or the update has no version.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ChangesOfAsync(LibraryUpdate update, CancellationToken cancellationToken)
    {
        if (update.VersionLabel is not { } version)
        {
            return null;
        }

        try
        {
            return await lookup.GetChangesAsync(update.File.Mod, version, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (McpProblems.Describe(exception) is not null)
        {
            // The update is listed without them.
            return null;
        }
    }
}
