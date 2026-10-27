using ModShelf.Application.Downloads;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Library;

/// <summary>
/// Finds the mod a link is to, as <see cref="Catalog.IModLookup"/> does, and changes it in the library through one
/// <see cref="ILibraryRepository.UpdateAsync"/>, so the change is whole.
/// </summary>
internal sealed class ModLibrary : IModLibrary
{
    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ModLibrary(ISourceRegistry sources, ILibraryRepository repository, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _sources = sources;
        _repository = repository;
        _timeProvider = timeProvider;
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

        // The site is asked first: the transaction mustn't wait on it.
        ModInfo info = await catalog.GetModAsync(mod, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        LibraryChange change = await _repository.UpdateAsync(
                mod,
                stored => stored is null
                    ? LibraryMod.Watch(mod, info.Name, info.Author, info.PageUrl, now)
                    : stored.WithDetails(info.Name, info.Author, info.PageUrl).Watched(),
                cancellationToken)
            .ConfigureAwait(false);
        return change.After!;
    }

    public async Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken)
    {
        ModRef mod = await ModOfAsync(link, cancellationToken).ConfigureAwait(false);
        return await _repository.UpdateAsync(mod, stored => stored?.Unwatched(), cancellationToken).ConfigureAwait(false);
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
