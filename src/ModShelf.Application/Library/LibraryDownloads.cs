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
/// Recording takes a lock, so the same job recorded twice at once, such as by the CLI and <see cref="LibraryRecorder"/>,
/// asks the site once. Placing a file doesn't: as for <see cref="LinkDownloads"/>, two callers adding files at the
/// same moment can still pick the same name. The CLI and a single desktop window never do.
/// </para>
/// </remarks>
internal sealed partial class LibraryDownloads : ILibraryDownloads, IDisposable
{
    private readonly ISourceRegistry _sources;
    private readonly IDownloadService _downloads;
    private readonly ILibraryRepository _library;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryDownloads> _logger;
    private readonly ConcurrentDictionary<DownloadJobId, SiteDetails> _details = new();
    private readonly SemaphoreSlim _recording = new(1, 1);

    public LibraryDownloads(
        ISourceRegistry sources,
        IDownloadService downloads,
        ILibraryRepository library,
        ISettingsService settings,
        ILibraryStorage storage,
        TimeProvider timeProvider,
        ILogger<LibraryDownloads> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _downloads = downloads;
        _library = library;
        _settings = settings;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The link must be absolute.", nameof(link));
        }

        RecognizedLink recognized;
        try
        {
            recognized = await _sources.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedLinkException exception)
        {
            throw new UnsupportedLinkException("No source knows this link.", exception);
        }

        ModFileRef file = recognized.File;
        IModCatalog catalog = _sources.Find<IModCatalog>(file.Mod.Game.Source)
            ?? throw new UnsupportedLinkException("This link isn't to a mod on a site whose mods ModShelf keeps in a library.");

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
            && await RecordAsync(missed, cancellationToken).ConfigureAwait(false) is { } recorded)
        {
            return new LibraryDownload(LibraryPaths.Resolve(root, recorded.RelativePath), null);
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
            string folder = stored is { Files.Count: > 0 } ? FolderOf(stored.Files[^1].RelativePath) : FolderOf(pathByPattern);
            relativePath = FreePath(root, library, jobs, folder, NameOf(pathByPattern), file);
        }

        string target = LibraryPaths.Resolve(root, relativePath);
        _storage.EnsurePathFits(target);
        return new LibraryDownload(target, await QueueAsync(recognized, link, target, details, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LibraryFile?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken)
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

        await _recording.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RecordCompletedAsync(job, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _recording.Release();
        }
    }

    public async Task RecordMissedAsync(CancellationToken cancellationToken)
    {
        string root;
        IReadOnlyList<LibraryMod> library;
        IReadOnlyList<DownloadJobSnapshot> jobs;
        try
        {
            root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
            jobs = await _downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantLookForMissed(_logger, exception);
            return;
        }

        Dictionary<ModFileRef, LibraryFile> kept = library.SelectMany(mod => mod.Files).ToDictionary(file => file.File);
        foreach (DownloadJobSnapshot job in jobs.Where(job => IsMissing(root, job, kept.GetValueOrDefault(job.File))))
        {
            try
            {
                await RecordAsync(job, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCantRecord(_logger, exception, job.FileName);
            }
        }
    }

    public void Dispose() => _recording.Dispose();

    private static async Task<SiteDetails> ReadDetailsAsync(IModCatalog catalog, ModFileRef file, CancellationToken cancellationToken)
    {
        Task<ModInfo> mod = catalog.GetModAsync(file.Mod, cancellationToken);
        Task<IReadOnlyList<ModFile>> files = catalog.GetFilesAsync(file.Mod, cancellationToken);
        await Task.WhenAll(mod, files).ConfigureAwait(false);

        ModFile listed = (await files.ConfigureAwait(false)).FirstOrDefault(candidate => candidate.Reference == file)
            ?? throw new ArgumentException(
                $"The site doesn't list file {file.Id} among the mod's files, so it can't go into the library.", nameof(file));
        return new SiteDetails(await mod.ConfigureAwait(false), listed);
    }

    private static bool IsDone(DownloadJobSnapshot job) => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled;

    private static string TargetOf(DownloadJobSnapshot job) => Path.Combine(job.TargetDirectory, job.FileName);

    /// <summary>
    /// The folder part of a library path, with no ending separator; empty for a file at the root.
    /// </summary>
    private static string FolderOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf(LibraryPaths.Separator);
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

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
        string withId = LibraryNames.File($"{Path.GetFileNameWithoutExtension(name)} ({file.Id}){Path.GetExtension(name)}");
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
    /// Whether the library's record of a file is of this download, or of one made after it, wherever that was saved.
    /// </summary>
    private static bool IsRecorded(LibraryFile kept, DownloadJobSnapshot job) => kept.DownloadedAt >= job.CreatedAt;

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

    private async Task<LibraryFile?> RecordCompletedAsync(DownloadJobSnapshot job, CancellationToken cancellationToken)
    {
        string root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
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
            _details.TryRemove(job.Id, out _);
            return kept;
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
        await _library.UpdateAsync(
                mod,
                current => (current, details) switch
                {
                    (null, null) => throw new InvalidOperationException("The mod left the library while its file was being added."),
                    (null, _) => LibraryMod.Watch(mod, details.Mod.Name, details.Mod.Author, details.Mod.PageUrl, now).WithFile(recorded),
                    (_, null) => current.WithFile(recorded),
                    _ => current.WithDetails(details.Mod.Name, details.Mod.Author, details.Mod.PageUrl).WithFile(recorded),
                },
                cancellationToken)
            .ConfigureAwait(false);

        _details.TryRemove(job.Id, out _);
        LogRecorded(_logger, recorded.FileName, mod.Id, mod.Game.Key);
        return recorded;
    }

    /// <summary>
    /// A mod's details and one of its files, as its site gave them.
    /// </summary>
    private sealed record SiteDetails(ModInfo Mod, ModFile File);
}
