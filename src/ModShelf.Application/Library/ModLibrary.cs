using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
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
    private readonly LibraryChanges _changes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModLibrary> _logger;

    public ModLibrary(
        ISourceRegistry sources,
        ILibraryRepository repository,
        ISettingsService settings,
        ILibraryStorage storage,
        LibraryChanges changes,
        TimeProvider timeProvider,
        ILogger<ModLibrary> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _storage = storage;
        _changes = changes;
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
        ModRef mod = await ModOfAsync(link, cancellationToken).ConfigureAwait(false);
        IModCatalog catalog = _sources.Find<IModCatalog>(mod.Game.Source)
            ?? throw new UnsupportedLinkException("This link isn't to a mod on a site where ModShelf can watch mods.");
        return await WatchingAsync(catalog, mod, cancellationToken).ConfigureAwait(false);
    }

    public Task<LibraryMod> WatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        IModCatalog catalog = _sources.Find<IModCatalog>(modRef.Game.Source)
            ?? throw new ArgumentException($"Source '{modRef.Game.Source}' isn't registered, or can't describe its mods.", nameof(modRef));
        return WatchingAsync(catalog, modRef, cancellationToken);
    }

    public async Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken)
    {
        // A mod in the library is found by its page there, so one its site removed can still be unwatched.
        ArgumentNullException.ThrowIfNull(link);
        ModRef mod = LibraryLinks.ModAt(await _repository.ListAsync(cancellationToken).ConfigureAwait(false), link)
            ?? await ModOfAsync(link, cancellationToken).ConfigureAwait(false);
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
        RemoveEmptiedFolders(root, path);
        return change;
    }

    public IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken) => _changes.Watch(cancellationToken);

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

    /// <exception cref="UnsupportedLinkException">No source knows the link.</exception>
    private async Task<ModRef> ModOfAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The link must be absolute.", nameof(link));
        }

        try
        {
            RecognizedLink recognized = await _sources.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
            return recognized.File.Mod;
        }
        catch (UnsupportedLinkException exception)
        {
            throw new UnsupportedLinkException("No source knows this link.", exception);
        }
    }
}
