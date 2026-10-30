using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Library;

/// <summary>
/// Works out where a file goes in the library, queues it through the download engine, and records it when it's done.
/// </summary>
/// <remarks>
/// <para>
/// The site's details read to place a file are kept in memory until its job is recorded or cancelled, so recording
/// needs no more requests. After a restart, they're read again.
/// </para>
/// <para>
/// Placing a file and recording one take one lock, which moving the library holds too (<see cref="ILibraryHold"/>): so
/// the same job recorded twice at once, such as by the CLI and <see cref="LibraryRecorder"/>, asks the site once; two
/// files added at once never pick the same name; and no file is placed or recorded under a root the library is leaving.
/// Other processes have locks of their own.
/// </para>
/// </remarks>
internal sealed partial class LibraryDownloads : ILibraryDownloads, ILibraryHold, IDisposable
{
    /// <summary>
    /// How many recent records are kept for a second caller, such as the CLI after the recorder.
    /// </summary>
    private const int RecentRecords = 64;

    private readonly ISourceRegistry _sources;
    private readonly IDownloadService _downloads;
    private readonly ILibraryRepository _library;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly LibraryChanges _changes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryDownloads> _logger;
    private readonly ConcurrentDictionary<DownloadJobId, SiteDetails> _details = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Used only under _lock.
    private readonly Dictionary<DownloadJobId, LibraryRecord> _recent = [];
    private readonly Queue<DownloadJobId> _recentOrder = new();

    public LibraryDownloads(
        ISourceRegistry sources,
        IDownloadService downloads,
        ILibraryRepository library,
        ISettingsService settings,
        ILibraryStorage storage,
        LibraryChanges changes,
        TimeProvider timeProvider,
        ILogger<LibraryDownloads> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _downloads = downloads;
        _library = library;
        _settings = settings;
        _storage = storage;
        _changes = changes;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken)
    {
        (RecognizedLink recognized, IModCatalog catalog) = await _sources.RecognizeForAsync<IModCatalog>(
                link, "This link isn't to a mod on a site whose mods ModShelf keeps in a library.", cancellationToken)
            .ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PlaceAsync(recognized, catalog, link, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LibraryRecord?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Status != DownloadJobStatus.Completed)
        {
            if (job.Status == DownloadJobStatus.Cancelled)
            {
                _details.TryRemove(job.Id, out _);
            }

            return null;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RecordCompletedAsync(job, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordMissedAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecordMissedHeldAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantLookForMissed(_logger, exception);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IDisposable> HoldAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (string root, int unrecorded) = await RecordMissedHeldAsync(cancellationToken).ConfigureAwait(false);
            if (unrecorded > 0)
            {
                string downloads = unrecorded == 1 ? $"A download into {root} finished" : $"{unrecorded} downloads into {root} finished";
                throw new InvalidOperationException(
                    $"{downloads}, but couldn't be added to the library just now, so the library can't move yet; the log says why. Try again later.");
            }

            return new Hold(_lock);
        }
        catch
        {
            _lock.Release();
            throw;
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// Works out where the file goes, and queues it there, with the lock taken.
    /// </summary>
    private async Task<LibraryDownload> PlaceAsync(RecognizedLink recognized, IModCatalog catalog, Uri link, CancellationToken cancellationToken)
    {
        ModFileRef file = recognized.File;
        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        string root = settings.LibraryRoot ?? _settings.DefaultLibraryRoot;
        IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        LibraryMod? stored = library.FirstOrDefault(mod => mod.Mod == file.Mod);
        LibraryFile? kept = stored?.Files.FirstOrDefault(candidate => candidate.File == file);

        if (kept is not null && _storage.FileExists(LibraryPaths.Resolve(root, kept.RelativePath)))
        {
            return new LibraryDownload(LibraryPaths.Resolve(root, kept.RelativePath), null);
        }

        // Queued for the library already, such as a download waiting for its handoff: it goes on where it was going.
        if (jobs.LastOrDefault(job => job.File == file && !IsDone(job) && LibraryPaths.RelativeTo(root, TargetOf(job)) is not null) is { } queued)
        {
            return new LibraryDownload(
                TargetOf(queued),
                await QueueAsync(recognized, link, TargetOf(queued), details: null, cancellationToken).ConfigureAwait(false));
        }

        // Downloaded already, but not recorded yet, such as just before the app stopped.
        if (jobs.LastOrDefault(job => job.File == file && IsMissing(root, job, kept)) is { } missed
            && await RecordCompletedAsync(missed, cancellationToken).ConfigureAwait(false) is { } recorded)
        {
            return new LibraryDownload(LibraryPaths.Resolve(root, recorded.File.RelativePath), null);
        }

        // A file the library has, but that's gone from disk, is downloaded again to the same place.
        SiteDetails? details = null;
        string relativePath;
        if (kept is not null)
        {
            relativePath = kept.RelativePath;
        }
        else
        {
            details = await ReadDetailsAsync(catalog, file, cancellationToken).ConfigureAwait(false);
            string pathByPattern = settings.FoldersFor(file.Mod.Game.Key).Render(
                file.Mod.Game.Source.Value, file.Mod.Game.Key, details.Mod.Name, file.Mod.Id, details.File.FileName);
            string folder = stored is { Files.Count: > 0 } ? ModFolderOf(stored.Files[^1].RelativePath) : FolderOf(pathByPattern);
            relativePath = FreePath(root, library, jobs, folder, NameOf(pathByPattern), file);
        }

        string target = LibraryPaths.Resolve(root, relativePath);
        _storage.EnsurePathFits(target);
        return new LibraryDownload(target, await QueueAsync(recognized, link, target, details, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<SiteDetails> ReadDetailsAsync(IModCatalog catalog, ModFileRef file, CancellationToken cancellationToken)
    {
        Task<ModInfo> mod = catalog.GetModAsync(file.Mod, cancellationToken);
        Task<IReadOnlyList<ModFile>> files = catalog.GetFilesAsync(file.Mod, cancellationToken);
        await Task.WhenAll(mod, files).ConfigureAwait(false);

        ModFile listed = (await files.ConfigureAwait(false)).FirstOrDefault(candidate => candidate.Reference == file)
            ?? throw new ArgumentException(
                $"The site doesn't list file {file.Id} among the mod's files, so it can't go into the library.", nameof(file));
        return new SiteDetails(await mod.ConfigureAwait(false), listed, await files.ConfigureAwait(false));
    }

    private static bool IsDone(DownloadJobSnapshot job) => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled;

    private static string TargetOf(DownloadJobSnapshot job) => Path.Combine(job.TargetDirectory, job.FileName);

    /// <summary>
    /// Whether the library's record of a file is of this download, or of one made after it, wherever that was saved.
    /// </summary>
    private static bool IsRecorded(LibraryFile kept, DownloadJobSnapshot job) => kept.DownloadedAt >= job.CreatedAt;

    /// <summary>
    /// The name a file gets when its own is taken: with the file's ID before its extension, as <c>SkyUI (35407).7z</c>.
    /// </summary>
    private static string WithId(string name, string fileId)
        => LibraryNames.File($"{Path.GetFileNameWithoutExtension(name)} ({fileId}){Path.GetExtension(name)}");

    /// <summary>
    /// The folder part of a library path, with no ending separator; empty for a file at the root.
    /// </summary>
    private static string FolderOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf(LibraryPaths.Separator);
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    /// <summary>
    /// The folder of a mod with a file at <paramref name="relativePath"/>: the file's folder, or the one holding its
    /// <c>_archive</c> folder.
    /// </summary>
    private static string ModFolderOf(string relativePath)
        => OldVersions.IsArchived(relativePath) ? FolderOf(FolderOf(relativePath)) : FolderOf(relativePath);

    /// <summary>
    /// The last part of a library path: the file's name.
    /// </summary>
    private static string NameOf(string relativePath) => relativePath[(relativePath.LastIndexOf(LibraryPaths.Separator) + 1)..];

    /// <summary>
    /// The file's path in <paramref name="folder"/>: under its own name; or, when that's taken, with its ID added.
    /// A name is taken when another file of the library, another unfinished download or a file on disk has it, in any
    /// case, since case doesn't matter on Windows and macOS.
    /// </summary>
    /// <param name="name">The file's name, made safe on every system.</param>
    /// <exception cref="IOException">Both names are taken.</exception>
    private string FreePath(
        string root,
        IReadOnlyList<LibraryMod> library,
        IReadOnlyList<DownloadJobSnapshot> jobs,
        string folder,
        string name,
        ModFileRef file)
    {
        string withId = WithId(name, file.Id);
        foreach (string candidate in new[] { name, withId })
        {
            string relativePath = folder.Length == 0 ? candidate : $"{folder}{LibraryPaths.Separator}{candidate}";
            string path = LibraryPaths.Resolve(root, relativePath);
            bool taken = library.SelectMany(mod => mod.Files).Any(other =>
                    other.File != file && string.Equals(other.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                || jobs.Any(job => job.File != file && !IsDone(job) && string.Equals(TargetOf(job), path, StringComparison.OrdinalIgnoreCase))
                || _storage.FileExists(path);
            if (!taken)
            {
                return relativePath;
            }
        }

        throw new IOException(
            $"There are files named {name} and {withId} in {(folder.Length == 0 ? root : LibraryPaths.Resolve(root, folder))} already, "
            + "so there's no name left for this one.");
    }

    /// <summary>
    /// Whether a job finished a download under the root that's still on disk, and that the library doesn't have as it is.
    /// </summary>
    /// <param name="kept">The library's record of the job's file, if any.</param>
    private bool IsMissing(string root, DownloadJobSnapshot job, LibraryFile? kept)
        => job.Status == DownloadJobStatus.Completed
            && LibraryPaths.RelativeTo(root, TargetOf(job)) is not null
            && !(kept is not null && IsRecorded(kept, job))
            && _storage.FileExists(TargetOf(job));

    /// <summary>
    /// Records the finished downloads under the library root that the library lacks and that are still on disk, with
    /// the lock taken. Each that can't be is logged.
    /// </summary>
    /// <returns>The root, and how many couldn't be recorded.</returns>
    private async Task<(string Root, int Unrecorded)> RecordMissedHeldAsync(CancellationToken cancellationToken)
    {
        string root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<ModFileRef, LibraryFile> kept = library.SelectMany(mod => mod.Files).ToDictionary(file => file.File);
        int unrecorded = 0;
        foreach (DownloadJobSnapshot job in jobs.Where(job => IsMissing(root, job, kept.GetValueOrDefault(job.File))))
        {
            try
            {
                await RecordCompletedAsync(job, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCantRecord(_logger, exception, job.FileName);
                unrecorded++;
            }
        }

        return (root, unrecorded);
    }

    private async Task<LinkDownload> QueueAsync(
        RecognizedLink recognized,
        Uri link,
        string target,
        SiteDetails? details,
        CancellationToken cancellationToken)
    {
        LinkDownload queued = await LinkDownloads.AddRecognizedAsync(
                _downloads, recognized, link, Path.GetDirectoryName(target)!, Path.GetFileName(target), cancellationToken)
            .ConfigureAwait(false);
        if (details is not null)
        {
            _details[queued.Job.Id] = details;
        }

        return queued;
    }

    private async Task<LibraryRecord?> RecordCompletedAsync(DownloadJobSnapshot job, CancellationToken cancellationToken)
    {
        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        string root = settings.LibraryRoot ?? _settings.DefaultLibraryRoot;
        if (LibraryPaths.RelativeTo(root, TargetOf(job)) is not { } relativePath)
        {
            _details.TryRemove(job.Id, out _);
            return null;
        }

        ModRef mod = job.File.Mod;
        long size = job.TotalBytes ?? job.BytesDownloaded;
        LibraryMod? stored = await _library.FindAsync(mod, cancellationToken).ConfigureAwait(false);
        LibraryFile? kept = stored?.Files.FirstOrDefault(file => file.File == job.File);
        if (kept is not null && IsRecorded(kept, job))
        {
            // Recorded already, perhaps just now by another caller, which then also did what the rule says.
            _details.TryRemove(job.Id, out _);
            return _recent.TryGetValue(job.Id, out LibraryRecord? recent) && recent.File.RelativePath == kept.RelativePath
                ? recent with { File = kept }
                : new LibraryRecord(kept, [], []);
        }

        // The site is asked first: the transaction mustn't wait on it. It's needed for a file the library doesn't have.
        SiteDetails? details = _details.GetValueOrDefault(job.Id);
        if (details is null && kept is null)
        {
            if (_sources.Find<IModCatalog>(mod.Game.Source) is not { } catalog)
            {
                return null;
            }

            details = await ReadDetailsAsync(catalog, job.File, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        LibraryFile recorded = details is null
            ? new LibraryFile(job.File, kept!.Name, kept.VersionLabel, kept.Category, kept.UploadedAt, relativePath, size, now)
            : new LibraryFile(
                job.File, details.File.Name, details.File.VersionLabel, details.File.Category, details.File.UploadedAt, relativePath, size, now);

        // Old versions are known from the site's list of files, which is read for a file new to the library. The library
        // never lists a file where it isn't, and no file is lost when it can't change: old versions are moved before it
        // changes, and moved back if it can't, and they leave it before they're deleted.
        OldVersionPlan plan = details is null || stored is null
            ? OldVersionPlan.None
            : OldVersions.Plan(stored, job.File, details.Files, settings.OldVersions ?? OldVersionRule.Default);
        IReadOnlyList<(LibraryFile Before, LibraryFile After)> archived = plan.Archive.Count == 0
            ? []
            : Archive(root, await _library.ListAsync(cancellationToken).ConfigureAwait(false), plan.Archive);
        LibraryChange change;
        try
        {
            change = await _library.UpdateAsync(
                    mod,
                    current =>
                    {
                        LibraryMod changed = (current, details) switch
                        {
                            (null, null) => throw new InvalidOperationException("The mod left the library while its file was being added."),
                            (null, _) => LibraryMod.Watch(mod, details.Mod.Name, details.Mod.Author, details.Mod.PageUrl, now).WithFile(recorded),
                            (_, null) => current.WithFile(recorded),
                            _ => current.WithDetails(details.Mod.Name, details.Mod.Author, details.Mod.PageUrl).WithFile(recorded),
                        };
                        foreach (LibraryFile gone in plan.Delete)
                        {
                            changed = changed.WithoutFile(gone.File)!;
                        }

                        foreach ((LibraryFile before, LibraryFile after) in archived.Where(moved => changed.Files.Any(file => file.File == moved.Before.File)))
                        {
                            changed = changed.WithFile(after);
                        }

                        return changed;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            PutBack(root, archived);
            throw;
        }

        _changes.Publish(change);
        _details.TryRemove(job.Id, out _);
        LogRecorded(_logger, recorded.FileName, mod.Id, mod.Game.Key);
        IReadOnlyList<LibraryFile> deleted = await DeleteAsync(root, mod, plan.Delete).ConfigureAwait(false);
        LibraryRecord record = new(recorded, [.. archived.Select(moved => moved.After)], deleted);
        Remember(job.Id, record);
        return record;
    }

    /// <summary>
    /// Keeps a record for whoever asks to record the same job next, forgetting the oldest beyond
    /// <see cref="RecentRecords"/>.
    /// </summary>
    private void Remember(DownloadJobId job, LibraryRecord record)
    {
        if (_recent.TryAdd(job, record))
        {
            _recentOrder.Enqueue(job);
        }
        else
        {
            _recent[job] = record;
        }

        while (_recentOrder.Count > RecentRecords)
        {
            _recent.Remove(_recentOrder.Dequeue());
        }
    }

    /// <summary>
    /// Moves old versions into the <c>_archive</c> folder in their folder. One that can't be moved, or whose path there
    /// would be too long for this system, stays as it is; a name another library file or a file on disk has there gets
    /// the file's ID added. One that isn't on disk stays as it is,
    /// unless it's in the <c>_archive</c> folder already at its size: moved there by a record that stopped before the
    /// library changed, such as when the app stopped.
    /// </summary>
    /// <returns>Each file moved, as it was and as it is now.</returns>
    private List<(LibraryFile Before, LibraryFile After)> Archive(string root, IReadOnlyList<LibraryMod> library, IReadOnlyList<LibraryFile> files)
    {
        List<(LibraryFile Before, LibraryFile After)> moved = [];
        HashSet<string> taken = new(library.SelectMany(mod => mod.Files).Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (LibraryFile file in files)
        {
            string from = LibraryPaths.Resolve(root, file.RelativePath);
            string withId = WithId(file.FileName, file.File.Id);
            string[] candidates = [.. new[] { file.FileName, withId }
                .Select(name => OldVersions.ArchivePath(file.RelativePath, name))
                .Where(candidate => !taken.Contains(candidate))];
            string? archivePath;
            if (!_storage.FileExists(from))
            {
                archivePath = candidates.FirstOrDefault(candidate => _storage.FileSize(LibraryPaths.Resolve(root, candidate)) == file.SizeInBytes);
                if (archivePath is null)
                {
                    continue;
                }

                LogFoundArchived(_logger, file.FileName);
            }
            else
            {
                string[] free = [.. candidates.Where(candidate => !_storage.FileExists(LibraryPaths.Resolve(root, candidate)))];
                archivePath = free.FirstOrDefault(candidate => Fits(LibraryPaths.Resolve(root, candidate)));
                if (archivePath is null)
                {
                    if (free.Length == 0)
                    {
                        LogNoArchiveName(_logger, file.FileName);
                    }
                    else
                    {
                        LogArchivePathTooLong(_logger, file.FileName);
                    }

                    continue;
                }

                try
                {
                    _storage.MoveFile(from, LibraryPaths.Resolve(root, archivePath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    LogCantArchive(_logger, exception, file.FileName);
                    continue;
                }

                LogArchived(_logger, file.FileName);
            }

            taken.Add(archivePath);
            moved.Add((file, new LibraryFile(
                file.File, file.Name, file.VersionLabel, file.Category, file.UploadedAt, archivePath, file.SizeInBytes, file.DownloadedAt)));
        }

        return moved;
    }

    /// <summary>
    /// Whether this system takes a library file at <paramref name="path"/>.
    /// </summary>
    private bool Fits(string path)
    {
        try
        {
            _storage.EnsurePathFits(path);
            return true;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes old versions the library doesn't list any more from the disk. One that can't be deleted, such as one another
    /// program has open, is listed again, so the next newer version tries again.
    /// </summary>
    /// <returns>The files deleted, or gone already.</returns>
    private async Task<IReadOnlyList<LibraryFile>> DeleteAsync(string root, ModRef mod, IReadOnlyList<LibraryFile> files)
    {
        List<LibraryFile> deleted = [];
        List<LibraryFile> kept = [];
        foreach (LibraryFile file in files)
        {
            try
            {
                _storage.DeleteFile(LibraryPaths.Resolve(root, file.RelativePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogCantDelete(_logger, exception, file.FileName);
                kept.Add(file);
                continue;
            }

            deleted.Add(file);
            LogDeleted(_logger, file.FileName);
        }

        if (kept.Count > 0)
        {
            await ListAgainAsync(mod, kept).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Lists old versions that couldn't be deleted again. When the library can't take that, they stay on disk unlisted,
    /// which loses no file.
    /// </summary>
    private async Task ListAgainAsync(ModRef mod, List<LibraryFile> kept)
    {
        try
        {
            // The library has changed already, so this isn't cancelled: it's quick, and leaves the library as the disk is.
            LibraryChange change = await _library.UpdateAsync(
                    mod,
                    current => current is null
                        ? null
                        : kept.Where(file => current.Files.All(listed => listed.File != file.File))
                            .Aggregate(current, (changed, file) => changed.WithFile(file)),
                    CancellationToken.None)
                .ConfigureAwait(false);
            _changes.Publish(change);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (LibraryFile file in kept)
            {
                LogCantListAgain(_logger, exception, file.FileName);
            }
        }
    }

    /// <summary>
    /// Moves archived files back where they were, when the library couldn't take the change.
    /// </summary>
    private void PutBack(string root, IReadOnlyList<(LibraryFile Before, LibraryFile After)> archived)
    {
        foreach ((LibraryFile before, LibraryFile after) in archived)
        {
            try
            {
                _storage.MoveFile(LibraryPaths.Resolve(root, after.RelativePath), LibraryPaths.Resolve(root, before.RelativePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogCantPutBack(_logger, exception, before.FileName, after.RelativePath);
            }
        }
    }

    /// <summary>
    /// A hold on the library, ended once when it's disposed.
    /// </summary>
    private sealed class Hold(SemaphoreSlim held) : IDisposable
    {
        private SemaphoreSlim? _held = held;

        public void Dispose() => Interlocked.Exchange(ref _held, null)?.Release();
    }

    /// <summary>
    /// A mod's details, one of its files, and all its files, as its site gave them.
    /// </summary>
    private sealed record SiteDetails(ModInfo Mod, ModFile File, IReadOnlyList<ModFile> Files);
}
