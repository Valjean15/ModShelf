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
/// Finds the mod a link is to, as <see cref="Catalog.IModLookup"/> does, and changes it in the library through one
/// <see cref="ILibraryRepository.UpdateAsync"/>, so the change is whole. Each change is published to
/// <see cref="ChangesAsync"/>.
/// </summary>
internal sealed partial class ModLibrary : IModLibrary
{
    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly IDownloadService _downloads;
    private readonly ICompletedDownloads _completed;
    private readonly LibraryChanges _changes;
    private readonly LibraryHistory _history;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModLibrary> _logger;

    public ModLibrary(
        ISourceRegistry sources,
        ILibraryRepository repository,
        ISettingsService settings,
        ILibraryStorage storage,
        IDownloadService downloads,
        ICompletedDownloads completed,
        LibraryChanges changes,
        LibraryHistory history,
        TimeProvider timeProvider,
        ILogger<ModLibrary> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _storage = storage;
        _downloads = downloads;
        _completed = completed;
        _changes = changes;
        _history = history;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken)
        => _repository.ListAsync(cancellationToken);

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return _repository.FindAsync(modRef, cancellationToken);
    }

    public async Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken)
    {
        (RecognizedLink recognized, IModCatalog catalog) = await _sources.RecognizeForAsync<IModCatalog>(
                link, "This link isn't to a mod on a site where ModShelf can watch mods.", cancellationToken)
            .ConfigureAwait(false);
        return await WatchingAsync(catalog, recognized.File.Mod, cancellationToken).ConfigureAwait(false);
    }

    public Task<LibraryMod> WatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        IModCatalog catalog = _sources.Require<IModCatalog>(modRef.Game.Source, "can't describe its mods", nameof(modRef));
        return WatchingAsync(catalog, modRef, cancellationToken);
    }

    public async Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken)
    {
        ModRef mod = await ModAtAsync(link, cancellationToken).ConfigureAwait(false);
        return await UpdateAsync(mod, stored => Unwatched(stored), cancellationToken).ConfigureAwait(false);
    }

    public Task<LibraryChange> SetWatchedAsync(ModRef modRef, bool watched, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return UpdateAsync(
            modRef,
            stored => stored is null
                ? throw new KeyNotFoundException($"Mod {modRef.Id} of {modRef.Game.Key} isn't in the library.")
                : watched ? stored.Watched() : Unwatched(stored),
            cancellationToken);
    }

    public async Task<LibraryChange> DeleteFileAsync(ModFileRef file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        LibraryFile kept = (await _repository.FindAsync(file.Mod, cancellationToken).ConfigureAwait(false))?.Files
                .FirstOrDefault(candidate => candidate.File == file)
            ?? throw new KeyNotFoundException($"The library has no file {file.Id} of mod {file.Mod.Id}.");
        string root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
        string path = LibraryPaths.Resolve(root, kept.RelativePath);

        // From disk first: a file that can't be deleted stays in the library, where the user can see it.
        _storage.DeleteFile(path);
        LibraryChange change = await UpdateAsync(file.Mod, stored => stored?.WithoutFile(file), cancellationToken).ConfigureAwait(false);
        LogDeletedFile(_logger, kept.FileName, file.Mod.Id, file.Mod.Game.Key);
        if (change.Before is { } before)
        {
            await _history.RecordAsync([LibraryEvent.Of(_timeProvider.GetUtcNow(), LibraryEventKind.Deleted, before, kept)], CancellationToken.None)
                .ConfigureAwait(false);
        }

        RemoveEmptiedFolders(root, path);
        return change;
    }

    public async Task<LibraryChange> ForgetAsync(Uri link, CancellationToken cancellationToken)
    {
        ModRef mod = await ModAtAsync(link, cancellationToken).ConfigureAwait(false);
        return await ForgetAsync(mod, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryChange> ForgetAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        LibraryMod stored = await _repository.FindAsync(modRef, cancellationToken).ConfigureAwait(false) ?? throw NotInLibrary(modRef);
        string root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot[] downloads = [.. (await _downloads.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.File.Mod == modRef && LibraryPaths.RelativeTo(root, Path.Combine(job.TargetDirectory, job.FileName)) is not null)];

        // An unfinished one would be recorded once it finishes, which adds the mod back.
        int unfinished = downloads.Count(job => job.Status is not (DownloadJobStatus.Completed or DownloadJobStatus.Cancelled));
        if (unfinished > 0)
        {
            throw new InvalidOperationException(unfinished == 1
                ? $"A download of a file of {stored.Name} into the library isn't finished. Let it finish, or cancel it, first."
                : $"{unfinished} downloads of files of {stored.Name} into the library aren't finished. Let them finish, or cancel them, first.");
        }

        // The completed ones first: left in the queue, the next start would record them again.
        HashSet<DownloadJobId> completed = [.. downloads.Where(job => job.Status == DownloadJobStatus.Completed).Select(job => job.Id)];
        int cleared = completed.Count > 0 ? await _completed.ClearCompletedAsync(completed, cancellationToken).ConfigureAwait(false) : 0;
        LibraryChange change = await UpdateAsync(modRef, current => current is null ? throw NotInLibrary(modRef) : null, cancellationToken)
            .ConfigureAwait(false);
        LogForgot(_logger, modRef.Id, modRef.Game.Key, change.Before!.Files.Count, cleared);
        await _history.RecordAsync([new LibraryEvent(_timeProvider.GetUtcNow(), LibraryEventKind.Removed, modRef, change.Before.Name)], CancellationToken.None)
            .ConfigureAwait(false);
        return change;
    }

    public IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken) => _changes.Watch(cancellationToken);

    private static KeyNotFoundException NotInLibrary(ModRef modRef)
        => new($"Mod {modRef.Id} of {modRef.Game.Key} on {modRef.Game.Source} isn't in the library.");

    /// <summary>
    /// Adds the mod to the library, watched, or watches it again, with its details as its site gives them now.
    /// </summary>
    private async Task<LibraryMod> WatchingAsync(IModCatalog catalog, ModRef mod, CancellationToken cancellationToken)
    {
        // The site is asked first: the transaction mustn't wait on it.
        ModInfo info = await catalog.GetModAsync(mod, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        LibraryChange change = await UpdateAsync(
                mod,
                stored => stored is null
                    ? LibraryMod.Watch(mod, info.Name, info.Author, info.PageUrl, now)
                    : stored.WithDetails(info.Name, info.Author, info.PageUrl).Watched(),
                cancellationToken)
            .ConfigureAwait(false);
        return change.After!;
    }

    /// <summary>
    /// The mod a link is to: a library mod by its page there, so one its site removed is found, or else as its site
    /// says.
    /// </summary>
    public Task<LibraryChange> SetPinnedAsync(ModRef modRef, bool pinned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return UpdateAsync(modRef, stored => (stored ?? throw NotInLibrary(modRef)).WithPinned(pinned), cancellationToken);
    }

    public async Task<LibraryChange> SetPinnedAsync(Uri link, bool pinned, CancellationToken cancellationToken)
    {
        ModRef mod = await ModAtAsync(link, cancellationToken).ConfigureAwait(false);
        return await SetPinnedAsync(mod, pinned, cancellationToken).ConfigureAwait(false);
    }

    public Task<LibraryChange> SkipUpdateAsync(ModFileRef update, bool skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        return UpdateAsync(update.Mod, stored => (stored ?? throw NotInLibrary(update.Mod)).WithUpdateSkipped(update, skip), cancellationToken);
    }

    public async Task<LibraryChange> SkipUpdateAsync(Uri link, bool skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        ModFileRef update = LibraryLinks.UpdateAt(await _repository.ListAsync(cancellationToken).ConfigureAwait(false), link)
            ?? (await _sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false)).File;
        return await SkipUpdateAsync(update, skip, cancellationToken).ConfigureAwait(false);
    }

    public Task<LibraryChange> SetNoteAsync(ModRef modRef, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return UpdateAsync(modRef, stored => (stored ?? throw NotInLibrary(modRef)).WithNote(note), cancellationToken);
    }

    public async Task<LibraryChange> SetNoteAsync(Uri link, string? note, CancellationToken cancellationToken)
    {
        ModRef mod = await ModAtAsync(link, cancellationToken).ConfigureAwait(false);
        return await SetNoteAsync(mod, note, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModRef> ModAtAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        return LibraryLinks.ModAt(await _repository.ListAsync(cancellationToken).ConfigureAwait(false), link)
            ?? (await _sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false)).File.Mod;
    }

    /// <summary>
    /// The mod no longer watched; the same mod when it wasn't, so nothing is told of a change that changed nothing.
    /// </summary>
    private static LibraryMod? Unwatched(LibraryMod? stored) => stored is { IsWatched: true } ? stored.Unwatched() : stored;

    /// <summary>
    /// Changes the mod in one transaction, and publishes what that did.
    /// </summary>
    private async Task<LibraryChange> UpdateAsync(ModRef modRef, Func<LibraryMod?, LibraryMod?> change, CancellationToken cancellationToken)
    {
        LibraryChange changed = await _repository.UpdateAsync(modRef, change, cancellationToken).ConfigureAwait(false);
        _changes.Publish(changed);
        return changed;
    }

    /// <summary>
    /// Removes the folders under <paramref name="root"/> that held the file at <paramref name="path"/>, as far up as
    /// they're empty. The root itself stays.
    /// </summary>
    private void RemoveEmptiedFolders(string root, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        while (folder is not null && LibraryPaths.RelativeTo(root, folder) is not null && _storage.RemoveFolderIfEmpty(folder))
        {
            folder = Path.GetDirectoryName(folder);
        }
    }
}
