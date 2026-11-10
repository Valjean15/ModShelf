using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Watchlist;

namespace ModShelf.Application.Watchlist;

/// <summary>
/// Finds the source a link belongs to, as <see cref="Catalog.IModLookup"/> does, and changes the mod on that source's
/// <see cref="IWatchlistSync"/>. Unfollowing finds a library mod by its page first (<see cref="LibraryLinks"/>). A source's
/// <see cref="IWatchlistReader"/> is read only.
/// </summary>
internal sealed class Watchlists : IWatchlists
{
    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _library;

    public Watchlists(ISourceRegistry sources, ILibraryRepository library)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(library);

        _sources = sources;
        _library = library;
    }

    public IReadOnlyList<SourceId> Sources => [.. _sources.All<IWatchlistReader>().Select(source => source.Id)];

    public bool CanChange(SourceId source) => source is not null && _sources.Find<IWatchlistSync>(source) is not null;

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        IWatchlistReader watchlist = _sources.Require<IWatchlistReader>(source, "has no watchlist", nameof(source));
        return watchlist.GetWatchedAsync(cancellationToken);
    }

    public Task<ModRef> WatchAsync(Uri link, CancellationToken cancellationToken)
        => ChangeAsync(link, inLibrary: false, (watchlist, mod) => watchlist.WatchAsync(mod, cancellationToken), cancellationToken);

    public Task<ModRef> UnwatchAsync(Uri link, CancellationToken cancellationToken)
        => ChangeAsync(link, inLibrary: true, (watchlist, mod) => watchlist.UnwatchAsync(mod, cancellationToken), cancellationToken);

    public Task WatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => Watchlist(modRef).WatchAsync(modRef, cancellationToken);

    public Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => Watchlist(modRef).UnwatchAsync(modRef, cancellationToken);

    /// <exception cref="ArgumentException">The mod's source isn't registered, or has no watchlist ModShelf can change.</exception>
    private IWatchlistSync Watchlist(ModRef modRef)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return _sources.Find<IWatchlistReader>(modRef.Game.Source) is { } reader and not IWatchlistSync
            ? throw new ArgumentException(ReadOnly(reader), nameof(modRef))
            : _sources.Require<IWatchlistSync>(modRef.Game.Source, "has no watchlist", nameof(modRef));
    }

    /// <summary>
    /// Such as "ModShelf can only read your subscribed mods on GameBanana: change them on its website.".
    /// </summary>
    private static string ReadOnly(IWatchlistReader reader)
        => $"ModShelf can only read your {reader.Words.Followed ?? "followed"} mods on {reader.DisplayName}: change them on its website.";

    /// <param name="inLibrary">Whether a link to a library mod's page is matched there first, without asking its site.</param>
    private async Task<ModRef> ChangeAsync(Uri link, bool inLibrary, Func<IWatchlistSync, ModRef, Task> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ModRef mod = (inLibrary ? LibraryLinks.ModAt(await _library.ListAsync(cancellationToken).ConfigureAwait(false), link) : null)
            ?? (await _sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false)).File.Mod;
        IWatchlistSync watchlist = _sources.Find<IWatchlistSync>(mod.Game.Source)
            ?? throw new UnsupportedLinkException(_sources.Find<IWatchlistReader>(mod.Game.Source) is { } reader
                ? ReadOnly(reader)
                : "This link isn't to a mod on a site where ModShelf can follow mods.");
        await change(watchlist, mod).ConfigureAwait(false);
        return mod;
    }
}
