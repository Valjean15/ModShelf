using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Watchlist;

namespace ModShelf.Application.Watchlist;

/// <summary>
/// Finds the source a link belongs to, as <see cref="Catalog.IModLookup"/> does, and changes the mod on that source's
/// <see cref="IWatchlistSync"/>.
/// </summary>
internal sealed class Watchlists : IWatchlists
{
    private readonly ISourceRegistry _sources;

    public Watchlists(ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    public IReadOnlyList<SourceId> Sources => [.. _sources.All<IWatchlistSync>().Select(source => source.Id)];

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        IWatchlistSync watchlist = _sources.Find<IWatchlistSync>(source)
            ?? throw new ArgumentException($"Source '{source}' isn't registered, or has no watchlist.", nameof(source));
        return watchlist.GetWatchedAsync(cancellationToken);
    }

    public Task<ModRef> WatchAsync(Uri link, CancellationToken cancellationToken)
        => ChangeAsync(link, (watchlist, mod) => watchlist.WatchAsync(mod, cancellationToken), cancellationToken);

    public Task<ModRef> UnwatchAsync(Uri link, CancellationToken cancellationToken)
        => ChangeAsync(link, (watchlist, mod) => watchlist.UnwatchAsync(mod, cancellationToken), cancellationToken);

    private async Task<ModRef> ChangeAsync(Uri link, Func<IWatchlistSync, ModRef, Task> change, CancellationToken cancellationToken)
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

        ModRef mod = recognized.File.Mod;
        IWatchlistSync watchlist = _sources.Find<IWatchlistSync>(mod.Game.Source)
            ?? throw new UnsupportedLinkException("This link isn't to a mod on a site where ModShelf can track mods.");
        await change(watchlist, mod).ConfigureAwait(false);
        return mod;
    }
}
