using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Updates;
using ModShelf.Cli.Commands;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools for the library: its mods and files, what came in lately, the updates, checking for them, watching
/// mods, forgetting them, and rescanning their folders.
/// </summary>
[McpServerToolType]
internal sealed class LibraryTools(
    IModLibrary library,
    ILibraryHistory history,
    ISettingsService settings,
    IUpdateChecker checker,
    IModLookup lookup,
    ILibraryRescan rescanner,
    ISourceRegistry sources,
    TimeProvider time)
{
    // The most mods a page of the library lists.
    private const int MaxLimit = 200;

    // The most of the library's history library_changes reads, the newest: a file downloaded before it says nothing of
    // what it replaced.
    private const int HistoryLimit = 1000;

    [McpServerTool(Name = "library_list", Title = "List the library", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the mods in ModShelf's library, the ones watched for updates and the ones with downloaded files, by site, game and name, a page at a time (offset and limit; hasMore says whether there's another page): each with its files (their absolute paths, versions, sizes, SHA-256, whether they're old versions archived, and whether they're missing from disk), the updates found for it (each saying whether it's skipped), whether it's pinned, and its note. A large library reads best a game or a mod at a time, or compact first, which gives counts instead of files and updates; watchedOnly, updatesOnly and missingOnly narrow it. A file without a SHA-256 was recorded before ModShelf kept them: library_rescan reads it.")]
    public Task<LibraryListing> ListAsync(
        [Description("Only this game's mods, as the site names it in its links, such as stardewvalley.")] string? game = null,
        [Description("Only this site's mods: nexus, gamebanana or modio.")] string? source = null,
        [Description("Only the mod with this ID, as in its link.")] string? mod = null,
        [Description("Only the mods watched for updates.")] bool watchedOnly = false,
        [Description("Only the mods with updates offered: found, and neither pinned nor skipped.")] bool updatesOnly = false,
        [Description("Only the mods with files missing from disk, which library_forget can take out of the library.")] bool missingOnly = false,
        [Description("Leave out each mod's files and updates, and give how many there are.")] bool compact = false,
        [Description("How many mods to skip: the offset of the page.")] int offset = 0,
        [Description("How many mods a page lists, from 1 to 200.")] int limit = 50,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            if (limit is < 1 or > MaxLimit)
            {
                throw new McpException($"limit must be from 1 to {MaxLimit}.");
            }

            if (offset < 0)
            {
                throw new McpException("offset must be 0 or more.");
            }

            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod[] matching =
            [
                .. mods
                    .Where(found => game is null || string.Equals(found.Mod.Game.Key, game.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Where(found => source is null || string.Equals(found.Mod.Game.Source.Value, source.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Where(found => mod is null || string.Equals(found.Mod.Id, mod.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Where(found => !watchedOnly || found.IsWatched)
                    .Where(found => !updatesOnly || found.OfferedUpdates.Count > 0)
                    .Where(found => !missingOnly || found.Files.Any(file => McpEntries.File(file, roots).Missing))
                    .OrderBy(found => found.Mod.Game.Source.Value, StringComparer.Ordinal)
                    .ThenBy(found => found.Mod.Game.Key, StringComparer.Ordinal)
                    .ThenBy(found => found.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(found => found.Mod.Id, StringComparer.Ordinal),
            ];
            return new LibraryListing(
                roots.Root,
                matching.Length,
                offset,
                (long)offset + limit < matching.Length,
                [.. matching.Skip(offset).Take(limit).Select(found => McpEntries.Mod(found, roots, compact))]);
        });

    [McpServerTool(Name = "library_changes", Title = "What's new in the library", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the files downloaded into the library since a time, the oldest first, each with the older version it replaced and what became of it (archived, deleted, kept, or gone from the library otherwise), and with its mod's other files in the library: where they are, and whether they're archived. Ask again from the result's 'until' to see only what's newer. What library.oldVersions does decides whether old versions stay, are archived or are deleted.")]
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
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            AppSettings stored = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryEvent> happened = await history.ListAsync(from, HistoryLimit, cancellationToken).ConfigureAwait(false);
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
                        McpEntries.File(found.File, roots),
                        [
                            .. found.Mod.Files
                                .Where(file => file.File != found.File.File)
                                .OrderByDescending(file => file.UploadedAt)
                                .Select(file => McpEntries.File(file, roots)),
                        ],
                        McpEntries.Replaced(found.Mod, found.File, happened, roots))),
            ];
            return new LibraryChanges(from, until, (stored.OldVersions ?? OldVersionRule.Default).ToString(), files);
        });

    [McpServerTool(Name = "library_updates", Title = "List the updates", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists the updates the last checks found for the watched mods, each with the link to download it and the library file it replaces, by mod name and the newest first. A pinned mod's updates and skipped updates aren't offered, so they're left out (library_list lists them). An update's kind is update for a newer version of a library file, or newFile for a file that replaces none, such as an optional file the mod's author added. With changelog, also what each update's version changes, from its mod's changelog, which asks each mod's site.")]
    public Task<UpdateListing> UpdatesAsync(
        [Description("Also read each update's changes from its mod's changelog.")] bool changelog = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<ModRef, LibraryMod> byRef = mods.ToDictionary(mod => mod.Mod);
            List<UpdateFound> updates = [];
            foreach (LibraryMod mod in mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (LibraryUpdate update in mod.OfferedUpdates.OrderByDescending(update => update.UploadedAt))
                {
                    IReadOnlyList<string>? changes = changelog ? await ChangesOfAsync(update, cancellationToken).ConfigureAwait(false) : null;
                    updates.Add(McpEntries.Found(update, byRef, roots, changes));
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
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<ModRef, LibraryMod> byRef = (await library.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(mod => mod.Mod);
            return new UpdateCheckResult(
                check.Checked,
                check.Requests,
                [.. check.Found.Select(update => McpEntries.Found(update, byRef, roots))],
                [.. check.Failures.Select(failure => new UpdateCheckFailureEntry(failure.Game.Source.Value, failure.Game.Key, failure.Mod?.Id, failure.Reason))]);
        });

    [McpServerTool(Name = "library_watch", Title = "Watch a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Watches a mod for updates, adding it to the library. Nothing is downloaded.")]
    public Task<WatchResult> WatchAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            Uri mod = McpProblems.Link(link);
            LibraryMod watched = await library.WatchAsync(mod, cancellationToken).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            return new WatchResult(true, $"Watching {watched.Name} ({CliText.ModOn(sources, watched.Mod)}).", McpEntries.Mod(watched, roots));
        });

    [McpServerTool(Name = "library_unwatch", Title = "Stop watching a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Stops watching a mod for updates. A mod with downloaded files stays in the library, and its files stay.")]
    public Task<WatchResult> UnwatchAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            Uri mod = McpProblems.Link(link);
            LibraryChange change = await library.UnwatchAsync(mod, cancellationToken).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            string message = change switch
            {
                { Before: null } => $"{CliText.Capitalized(CliText.ModOn(sources, change.Mod))} isn't in the library.",
                { Before.IsWatched: false } => $"{change.Before.Name} isn't watched.",
                { After: null } => $"Stopped watching {change.Before.Name}. It had no downloaded files, so it's out of the library.",
                _ => $"Stopped watching {change.Before.Name}. Its downloaded files stay in the library.",
            };
            return new WatchResult(false, message, change.After is { } after ? McpEntries.Mod(after, roots) : null);
        });

    [McpServerTool(Name = "library_pin", Title = "Pin a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Pins a library mod at the version it has, or unpins it: a pinned mod's updates are still found, but aren't offered, so library_updates, library_check and the desktop app leave them out. Nothing is downloaded or deleted.")]
    public Task<PinResult> PinAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        [Description("False to unpin it.")] bool pinned = true,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            LibraryChange change = await InLibraryAsync(() => library.SetPinnedAsync(McpProblems.Link(link), pinned, cancellationToken)).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod mod = change.After!;
            string message = (pinned, change.Before!.IsPinned) switch
            {
                (true, true) => $"{mod.Name} is pinned already.",
                (true, false) => $"Pinned {mod.Name}: its updates are still found, but aren't offered.",
                (false, false) => $"{mod.Name} isn't pinned.",
                _ => $"Unpinned {mod.Name}: its updates are offered again.",
            };
            return new PinResult(mod.IsPinned, message, McpEntries.Mod(mod, roots));
        });

    [McpServerTool(Name = "library_skip", Title = "Skip an update", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Skips one of a library mod's updates until a newer one comes, so it isn't offered, or offers it again. Nothing is downloaded or deleted.")]
    public Task<SkipResult> SkipAsync(
        [Description("The update's link, as library_updates gives it.")] string link,
        [Description("False to offer it again.")] bool skip = true,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            LibraryChange change = await InLibraryAsync(() => library.SkipUpdateAsync(McpProblems.Link(link), skip, cancellationToken)).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod before = change.Before!;
            LibraryMod mod = change.After!;
            ModFileRef? changed = skip
                ? mod.SkippedUpdates.FirstOrDefault(file => !before.SkippedUpdates.Contains(file))
                : before.SkippedUpdates.FirstOrDefault(file => !mod.SkippedUpdates.Contains(file));
            LibraryUpdate? update = changed is null ? null : mod.Updates.First(found => found.File == changed);
            string message = (skip, update) switch
            {
                (true, null) => $"That update of {mod.Name} is skipped already.",
                (false, null) => $"That update of {mod.Name} isn't skipped.",
                (true, _) => $"Skipped {update.Name} of {mod.Name} until a newer update comes.",
                _ => $"{update.Name} of {mod.Name} is offered again.",
            };
            return new SkipResult(skip, message, update is null ? null : McpEntries.Update(update, mod, roots), McpEntries.Mod(mod, roots));
        });

    [McpServerTool(Name = "library_note", Title = "Keep a note of a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Keeps the user's note of a library mod, such as why it's pinned, in place of the one it has; no note removes it. library_list and mod_show give the notes.")]
    public Task<NoteResult> NoteAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        [Description("The note, up to 2,000 characters; none, or blank, to remove the mod's note.")] string? note = null,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            LibraryChange change = await InLibraryAsync(() => library.SetNoteAsync(McpProblems.Link(link), note, cancellationToken)).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod mod = change.After!;
            string message = (change.Before!.Note, mod.Note) switch
            {
                (null, null) => $"{mod.Name} has no note.",
                (_, null) => $"Removed the note of {mod.Name}.",
                _ => $"Kept the note of {mod.Name}.",
            };
            return new NoteResult(mod.Note, message, McpEntries.Mod(mod, roots));
        });

    [McpServerTool(Name = "library_forget", Title = "Forget a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Takes a mod out of ModShelf's library, with its file records and its updates, so it isn't watched any more, such as a mod the user no longer uses. Nothing on disk is touched: its downloaded files stay where they are, no longer the library's, and its completed downloads leave the download queue. Refused while a download of one of its files into the library isn't finished.")]
    public Task<ForgetResult> ForgetAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            Uri mod = McpProblems.Link(link);
            LibraryChange change;
            try
            {
                change = await library.ForgetAsync(mod, cancellationToken).ConfigureAwait(false);
            }
            catch (KeyNotFoundException exception)
            {
                // Nothing to forget, as unwatching a mod that isn't watched.
                return new ForgetResult(false, exception.Message, null);
            }
            catch (InvalidOperationException exception)
            {
                throw new McpException(exception.Message, exception);
            }

            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod forgotten = change.Before!;
            string files = forgotten.Files.Count switch
            {
                0 => "It had no downloaded files.",
                1 => "Its downloaded file stays on disk, at the path in mod.files.",
                _ => string.Create(CultureInfo.InvariantCulture, $"Its {forgotten.Files.Count} downloaded files stay on disk, at the paths in mod.files."),
            };
            return new ForgetResult(
                true,
                $"{forgotten.Name} is out of the library: ModShelf doesn't watch it or keep track of its files any more. {files}",
                McpEntries.Mod(forgotten, roots));
        });

    [McpServerTool(Name = "library_rescan", Title = "Rescan the library", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Rescans the mods' folders: adds the files there that the library doesn't know, such as ones placed by hand, when they match one of the mod's files on its site by name and size, and reads the SHA-256 of the library's files that have none. Says which files are missing from disk, and which in the folders match nothing. With a link, one mod, which comes into the library, watched, if its files are found where the folder pattern puts them; without, every mod in the library. Nothing is moved, renamed or deleted, and the site is asked only about mods with files to match.")]
    public Task<RescanResult> RescanAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana or mod.io; leave it out to rescan the whole library.")] string? link = null,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            Uri? mod = link is null ? null : McpProblems.Link(link);
            LibraryRescan rescan = await rescanner.RescanAsync(mod, cancellationToken).ConfigureAwait(false);
            LibraryRoots roots = await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            RescannedFile Entry(LibraryFile file)
                => new(file.File.Mod.Game.Source.Value, file.File.Mod.Game.Key, file.File.Mod.Id, McpEntries.File(file, roots));

            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"Rescanned {rescan.Mods} {(rescan.Mods == 1 ? "mod" : "mods")}: {rescan.Added.Count} files added, {rescan.Hashed.Count} hashed, {rescan.Missing.Count} missing from disk, {rescan.Unmatched.Count} matching nothing");
            return new RescanResult(
                rescan.Failures.Count == 0 ? $"{message}." : string.Create(CultureInfo.InvariantCulture, $"{message}; {rescan.Failures.Count} mods' files couldn't be matched."),
                rescan.Mods,
                [.. rescan.Added.Select(Entry)],
                [.. rescan.Hashed.Select(Entry)],
                [.. rescan.Missing.Select(Entry)],
                rescan.Unmatched,
                [.. rescan.Failures.Select(failure => new RescanFailureEntry(failure.Mod.Game.Source.Value, failure.Mod.Game.Key, failure.Mod.Id, failure.Reason))]);
        });

    /// <summary>
    /// Changes a mod in the library, saying so when it isn't in it.
    /// </summary>
    private static async Task<LibraryChange> InLibraryAsync(Func<Task<LibraryChange>> change)
    {
        try
        {
            return await change().ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

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
