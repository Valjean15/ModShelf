using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// Lists what could go from the library's folders, asking each mod's site at most once which files it still offers, and
/// deletes what the user chooses through <see cref="IModLibrary.DeleteFileAsync"/>, one file at a time (ADR 0104).
/// </summary>
internal sealed partial class LibraryCleanup : ILibraryCleanup
{
    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly IModLibrary _library;
    private readonly ILibraryHold _hold;
    private readonly LibraryHistory _history;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryCleanup> _logger;

    public LibraryCleanup(
        ISourceRegistry sources,
        ILibraryRepository repository,
        ISettingsService settings,
        ILibraryStorage storage,
        IModLibrary library,
        ILibraryHold hold,
        LibraryHistory history,
        TimeProvider timeProvider,
        ILogger<LibraryCleanup> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _storage = storage;
        _library = library;
        _hold = hold;
        _history = history;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CleanupPlan> PlanAsync(GameRef? game, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        LibraryRoots roots = LibraryRoots.Of(settings, _settings.DefaultLibraryRoot);
        IReadOnlyList<LibraryMod> library = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        LibraryMod[] mods = [.. library.Where(mod => game is null || AppSettings.GameComparer.Equals(mod.Mod.Game, game))];

        // Only what's on disk, at the size it has there: a file that's gone frees nothing.
        OnDisk[] onDisk =
        [
            .. library.SelectMany(mod => mod.Files.Select(file => (Mod: mod, File: file, Path: roots.Resolve(file))))
                .Select(found => _storage.FileSize(found.Path) is { } size ? new OnDisk(found.Mod, found.File, found.Path, size) : null)
                .OfType<OnDisk>(),
        ];

        List<CleanupEntry> entries = [];
        HashSet<OnDisk> duplicates = Duplicates(onDisk, mods, entries);
        List<(CleanupGroup Group, OnDisk Found)> libraryFiles =
        [
            .. onDisk.Where(found => !duplicates.Contains(found) && mods.Contains(found.Mod))
                .Select(found => (Group: GroupOf(found), Found: found))
                .Where(candidate => candidate.Group is not null)
                .Select(candidate => (candidate.Group!.Value, candidate.Found)),
        ];
        Dictionary<string, List<LibraryMod>> unknown = Unknown(roots, settings, library, mods);

        // Each mod's site is asked once at most, and not about a mod it removed.
        LibraryMod[] toAsk =
        [
            .. mods.Where(mod => !mod.IsGone
                    && (libraryFiles.Any(candidate => candidate.Found.Mod == mod) || unknown.Values.Any(holders => holders.Contains(mod))))
                .OrderBy(mod => mod.Mod.Game.ToString(), StringComparer.Ordinal)
                .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.Mod.Id, StringComparer.Ordinal),
        ];
        Dictionary<LibraryMod, Answer> answers = await AskAsync(toAsk, progress, cancellationToken).ConfigureAwait(false);
        int asked = answers.Values.Count(answer => answer.Asked);

        foreach ((CleanupGroup group, OnDisk found) in libraryFiles)
        {
            entries.Add(Kept(Entry(group, found), found.Mod, answers, file => file.Reference == found.File.File));
        }

        foreach ((string path, List<LibraryMod> holders) in unknown)
        {
            if (Unmatched(roots, path, holders, answers) is { } entry)
            {
                entries.Add(entry);
            }
        }

        CleanupPlan plan = new(
            game,
            [
                .. entries.OrderBy(entry => entry.Group)
                    .ThenBy(entry => entry.Mod.Game.ToString(), StringComparer.Ordinal)
                    .ThenBy(entry => entry.ModName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal),
            ],
            asked);
        int canGo = plan.Entries.Count(entry => entry.CanGo);
        LogPlanned(_logger, canGo, plan.CanFree, plan.Entries.Count - canGo, plan.Asked);
        return plan;
    }

    public async Task<CleanupResult> DeleteAsync(IReadOnlyList<CleanupEntry> entries, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.FirstOrDefault(entry => !entry.CanGo) is { } kept)
        {
            throw new ArgumentException($"{kept.FileName} is kept, so it isn't deleted.", nameof(entries));
        }

        List<CleanupEntry> deleted = [];
        List<CleanupFailure> failures = [];
        using (await _hold.HoldAsync(cancellationToken).ConfigureAwait(false))
        {
            // Held, nothing comes into the library, so what it has now is what it has while files go.
            LibraryRoots roots = await _settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
            HashSet<string> known = new(
                (await _repository.ListAsync(cancellationToken).ConfigureAwait(false)).SelectMany(mod => mod.Files).Select(roots.Resolve),
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CleanupEntry entry = entries[index];
                if (await ChangedAsync(entry, roots, known, cancellationToken).ConfigureAwait(false) is { } changed)
                {
                    failures.Add(new CleanupFailure(entry, changed));
                }
                else if (await DeleteAsync(entry, roots, cancellationToken).ConfigureAwait(false) is { } failure)
                {
                    failures.Add(new CleanupFailure(entry, failure));
                }
                else
                {
                    deleted.Add(entry);
                }

                progress?.Report(new CleanupProgress(index + 1, entries.Count));
            }
        }

        CleanupResult result = new(deleted, failures);
        LogDeleted(_logger, deleted.Count, result.Freed, failures.Count);
        return result;
    }

    private static bool StopsTheSource(Exception exception)
        => exception is AuthenticationFailedException
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// The group a library file that isn't a duplicate is in; null for none, when it's a watched mod's current file.
    /// </summary>
    private static CleanupGroup? GroupOf(OnDisk found)
        => OldVersions.IsArchived(found.File.RelativePath) ? CleanupGroup.Archived
            : !found.Mod.IsWatched ? CleanupGroup.Unwatched
            : null;

    private static CleanupEntry Entry(CleanupGroup group, OnDisk found)
        => new(group, found.Path, found.File.RelativePath, found.Size, found.Mod.Mod, found.Mod.Name, found.File);

    /// <summary>
    /// Finds the library files with the same SHA-256 and size as another, across the whole library, and adds those of
    /// <paramref name="mods"/> that aren't the copy kept to <paramref name="entries"/>.
    /// </summary>
    /// <returns>Every duplicate that isn't the copy kept, of any mod.</returns>
    private static HashSet<OnDisk> Duplicates(IReadOnlyList<OnDisk> onDisk, LibraryMod[] mods, List<CleanupEntry> entries)
    {
        HashSet<OnDisk> duplicates = [];
        foreach (IGrouping<(string, long), OnDisk> same in onDisk
            .Where(found => found.File.Sha256 is not null && found.Size > 0)
            .GroupBy(found => (found.File.Sha256!, found.Size))
            .Where(same => same.Skip(1).Any()))
        {
            OnDisk kept = same
                .OrderBy(found => OldVersions.IsArchived(found.File.RelativePath))
                .ThenByDescending(found => found.Mod.IsWatched)
                .ThenByDescending(found => found.File.DownloadedAt)
                .ThenBy(found => found.Path, StringComparer.Ordinal)
                .First();
            CleanupCopy copy = new(kept.Path, kept.Mod.Mod, kept.Mod.Name, kept.File);
            foreach (OnDisk other in same.Where(found => found != kept))
            {
                duplicates.Add(other);
                if (mods.Contains(other.Mod))
                {
                    entries.Add(Entry(CleanupGroup.Duplicates, other) with { SameAs = copy });
                }
            }
        }

        return duplicates;
    }

    /// <summary>
    /// The files in <paramref name="mods"/>' folders that the library doesn't have, by full path, each with the mods
    /// whose folders it's in.
    /// </summary>
    private Dictionary<string, List<LibraryMod>> Unknown(LibraryRoots roots, AppSettings settings, IReadOnlyList<LibraryMod> library, LibraryMod[] mods)
    {
        // In any case, since case doesn't matter on Windows and macOS, as a rescan has it.
        HashSet<string> known = new(library.SelectMany(mod => mod.Files).Select(roots.Resolve), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<LibraryMod>> unknown = new(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryMod mod in mods)
        {
            foreach (string path in ModFolders.Of(roots, settings, mod.Mod, mod, mod.Name).SelectMany(ListFiles).Where(path => !known.Contains(path)))
            {
                if (!unknown.TryGetValue(path, out List<LibraryMod>? holders))
                {
                    unknown[path] = holders = [];
                }

                if (!holders.Contains(mod))
                {
                    holders.Add(mod);
                }
            }
        }

        return unknown;
    }

    /// <summary>
    /// The entry of a file the library doesn't have: unmatched when every mod whose folder it's in was asked and none
    /// has a file it matches; null when it's no longer on disk.
    /// </summary>
    private CleanupEntry? Unmatched(LibraryRoots roots, string path, List<LibraryMod> holders, Dictionary<LibraryMod, Answer> answers)
    {
        LibraryMod mod = holders.OrderBy(holder => holder.Name, StringComparer.OrdinalIgnoreCase).ThenBy(holder => holder.Mod.Id, StringComparer.Ordinal).First();
        if (_storage.FileSize(path) is not { } size || roots.RelativeTo(mod.Mod.Game, path) is not { } relativePath)
        {
            return null;
        }

        // As a rescan matches it: by name and size, to a file the mod doesn't have yet.
        string name = Path.GetFileName(path);
        CleanupEntry entry = new(CleanupGroup.Unmatched, path, relativePath, size, mod.Mod, mod.Name);
        foreach (LibraryMod holder in holders)
        {
            if (answers.TryGetValue(holder, out Answer? answer) && answer.Files is { } files
                && files.FirstOrDefault(file => holder.Files.All(have => have.File != file.Reference) && SiteFiles.Matches(name, size, file)) is { } match)
            {
                return entry with { Mod = holder.Mod, ModName = holder.Name, Kept = KeptBecause.MatchesSiteFile, KeptDetail = match.Name };
            }
        }

        if (holders.Any(holder => holder.IsGone))
        {
            return entry with { Kept = KeptBecause.ModGone };
        }

        return holders.Select(holder => answers.GetValueOrDefault(holder)?.Failure).FirstOrDefault(failure => failure is not null) is { } why
            ? entry with { Kept = KeptBecause.NotChecked, KeptDetail = why }
            : entry;
    }

    /// <summary>
    /// The entry of an archived or unwatched library file, kept when it may be the only copy left.
    /// </summary>
    private static CleanupEntry Kept(CleanupEntry entry, LibraryMod mod, Dictionary<LibraryMod, Answer> answers, Func<ModFile, bool> isIt)
    {
        if (mod.IsGone)
        {
            return entry with { Kept = KeptBecause.ModGone };
        }

        Answer answer = answers[mod];
        if (answer.Files is not { } files)
        {
            return entry with { Kept = KeptBecause.NotChecked, KeptDetail = answer.Failure };
        }

        // An archived file is listed, but can't be downloaded.
        return files.Any(file => isIt(file) && file.Category != ModFileCategory.Archived) ? entry : entry with { Kept = KeptBecause.NotOffered };
    }

    /// <summary>
    /// Asks each mod's site for the files it lists, until a refused account or a request to slow down stops asking it.
    /// </summary>
    private async Task<Dictionary<LibraryMod, Answer>> AskAsync(LibraryMod[] mods, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        Dictionary<LibraryMod, Answer> answers = [];
        Dictionary<SourceId, string> stopped = [];
        progress?.Report(new CleanupProgress(0, mods.Length));
        for (int index = 0; index < mods.Length; index++)
        {
            LibraryMod mod = mods[index];
            answers[mod] = await AskAsync(mod.Mod, stopped, cancellationToken).ConfigureAwait(false);
            progress?.Report(new CleanupProgress(index + 1, mods.Length));
        }

        return answers;
    }

    private async Task<Answer> AskAsync(ModRef mod, Dictionary<SourceId, string> stopped, CancellationToken cancellationToken)
    {
        if (_sources.Find<IModCatalog>(mod.Game.Source) is not { } catalog)
        {
            return Answer.Failed($"{mod.Game.Source} can't list its mods' files, so it can't say which it still offers.");
        }

        if (stopped.TryGetValue(mod.Game.Source, out string? why))
        {
            return Answer.Failed($"Not asked: {mod.Game.Source} refused an earlier question. {why}");
        }

        try
        {
            return new Answer(await catalog.GetFilesAsync(mod, cancellationToken).ConfigureAwait(false), null, Asked: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantAsk(_logger, exception, mod.Id, mod.Game.Key);
            if (StopsTheSource(exception))
            {
                stopped[mod.Game.Source] = exception.Message;
            }

            return new Answer(null, exception.Message, Asked: true);
        }
    }

    /// <summary>
    /// The files in a folder; none when it can't be listed, which is logged.
    /// </summary>
    private IReadOnlyList<string> ListFiles(string folder)
    {
        try
        {
            return _storage.FilesIn(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantList(_logger, exception, folder);
            return [];
        }
    }

    /// <summary>
    /// Why a file listed to go is left: how it changed since it was listed; null when it's as it was.
    /// </summary>
    private async Task<string?> ChangedAsync(CleanupEntry entry, LibraryRoots roots, HashSet<string> known, CancellationToken cancellationToken)
    {
        const string ListAgain = "List what could go again to see it as it is now.";

        if (roots.RelativeTo(entry.Mod.Game, entry.Path) != entry.RelativePath)
        {
            return $"Its game's folder changed since it was listed. {ListAgain}";
        }

        if (_storage.FileSize(entry.Path) is not { } size)
        {
            return "It's no longer on disk.";
        }

        if (size != entry.SizeInBytes)
        {
            return $"It changed since it was listed. {ListAgain}";
        }

        if (entry.File is null)
        {
            return known.Contains(entry.Path) ? $"It's in the library now. {ListAgain}" : null;
        }

        LibraryMod? stored = await _repository.FindAsync(entry.Mod, cancellationToken).ConfigureAwait(false);
        if (stored?.Files.FirstOrDefault(file => file.File == entry.File.File) is not { } current || current.RelativePath != entry.RelativePath)
        {
            return $"It's no longer in the library where it was listed. {ListAgain}";
        }

        if (entry.Group == CleanupGroup.Unwatched && stored.IsWatched)
        {
            return $"Its mod is watched again. {ListAgain}";
        }

        if (entry.SameAs is { } copy)
        {
            LibraryMod? holder = await _repository.FindAsync(copy.Mod, cancellationToken).ConfigureAwait(false);
            bool same = holder?.Files.FirstOrDefault(file => file.File == copy.File.File) is { } kept
                && kept.RelativePath == copy.File.RelativePath
                && kept.Sha256 is not null
                && kept.Sha256 == current.Sha256
                && roots.Resolve(kept) == copy.Path
                && _storage.FileSize(copy.Path) == size;
            return same ? null : $"The copy kept, {copy.Path}, changed or went since it was listed. {ListAgain}";
        }

        return null;
    }

    /// <summary>
    /// Deletes a file: the library's through the library, any other from disk, with the folders it leaves empty.
    /// </summary>
    /// <returns>Why it couldn't be deleted; null once it is.</returns>
    private async Task<string?> DeleteAsync(CleanupEntry entry, LibraryRoots roots, CancellationToken cancellationToken)
    {
        try
        {
            if (entry.File is { } file)
            {
                await _library.DeleteFileAsync(file.File, cancellationToken).ConfigureAwait(false);
                return null;
            }

            _storage.DeleteFile(entry.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantDelete(_logger, exception, entry.FileName);
            return exception.Message;
        }
        catch (KeyNotFoundException)
        {
            return "It's no longer in the library.";
        }

        LogDeletedUnmatched(_logger, entry.FileName, entry.Mod.Id, entry.Mod.Game.Key);
        RemoveEmptiedFolders(roots.For(entry.Mod.Game), entry.Path);
        await _history.RecordAsync(
                [new LibraryEvent(_timeProvider.GetUtcNow(), LibraryEventKind.Deleted, entry.Mod, entry.ModName, FileName: entry.FileName, RelativePath: entry.RelativePath)],
                CancellationToken.None)
            .ConfigureAwait(false);
        return null;
    }

    private void RemoveEmptiedFolders(string root, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        while (folder is not null && LibraryPaths.RelativeTo(root, folder) is not null && _storage.RemoveFolderIfEmpty(folder))
        {
            folder = Path.GetDirectoryName(folder);
        }
    }

    /// <summary>
    /// A library file on disk, with its size there.
    /// </summary>
    private sealed record OnDisk(LibraryMod Mod, LibraryFile File, string Path, long Size);

    /// <summary>
    /// What a mod's site said: the files it lists, or why it couldn't be asked; and whether it was asked.
    /// </summary>
    private sealed record Answer(IReadOnlyList<ModFile>? Files, string? Failure, bool Asked = false)
    {
        public static Answer Failed(string why) => new(null, why);
    }
}
