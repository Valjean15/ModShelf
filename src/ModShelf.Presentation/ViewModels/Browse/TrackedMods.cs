using ModShelf.Application.Watchlist;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The mods the linked accounts track on their sites, as far as the Browse screen knows: each site's list is read once,
/// when first needed, and kept up to date with what the screen tracks and untracks.
/// </summary>
internal sealed class TrackedMods
{
    private readonly IWatchlists _watchlists;
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceId, HashSet<ModRef>> _bySource = [];

    public TrackedMods(IWatchlists watchlists)
    {
        ArgumentNullException.ThrowIfNull(watchlists);

        _watchlists = watchlists;
    }

    /// <summary>
    /// Whether the site has a list of tracked mods.
    /// </summary>
    public bool CanTrack(SourceId source) => _watchlists.Sources.Contains(source);

    /// <summary>
    /// Whether the mod is tracked; null while its site's list hasn't been read.
    /// </summary>
    public bool? IsTracked(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        lock (_gate)
        {
            return _bySource.TryGetValue(mod.Game.Source, out HashSet<ModRef>? tracked) ? tracked.Contains(mod) : null;
        }
    }

    /// <summary>
    /// Reads the site's list afresh, and keeps it.
    /// </summary>
    /// <inheritdoc cref="IWatchlists.GetWatchedAsync" path="/exception"/>
    public async Task<IReadOnlyList<ModRef>> ReadAsync(SourceId source, CancellationToken cancellationToken)
    {
        IReadOnlyList<ModRef> tracked = await _watchlists.GetWatchedAsync(source, cancellationToken);
        lock (_gate)
        {
            _bySource[source] = [.. tracked];
        }

        return tracked;
    }

    /// <summary>
    /// Tracks the mod on its site, or stops tracking it.
    /// </summary>
    /// <inheritdoc cref="IWatchlists.WatchAsync(ModRef, CancellationToken)" path="/exception"/>
    public async Task SetAsync(ModRef mod, bool tracked, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mod);

        await (tracked ? _watchlists.WatchAsync(mod, cancellationToken) : _watchlists.UnwatchAsync(mod, cancellationToken));
        lock (_gate)
        {
            if (_bySource.TryGetValue(mod.Game.Source, out HashSet<ModRef>? list))
            {
                if (tracked)
                {
                    list.Add(mod);
                }
                else
                {
                    list.Remove(mod);
                }
            }
        }
    }
}
