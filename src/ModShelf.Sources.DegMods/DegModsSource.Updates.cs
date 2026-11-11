using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.DegMods.Catalog;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The <see cref="IUpdateFeed"/> half: the game's mods whose event changed since a time, from one request to each relay
/// for every mod changed since, read afresh. An event says nothing of what changed, so each is a mod to check.
/// </summary>
public sealed partial class DegModsSource : IUpdateFeed
{
    // A month of changes is a few hundred events at most.
    private static readonly TimeSpan UpdateReach = TimeSpan.FromDays(28);

    // A since the caller read from its own clock a moment before this source reads it is still taken.
    private static readonly TimeSpan ReachGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 28 days, as Nexus Mods' and mod.io's feeds.
    /// </summary>
    public TimeSpan Reach => UpdateReach;

    /// <returns>The game's mods DEG Mods shows whose newest event was made since <paramref name="since"/>.</returns>
    /// <exception cref="ArgumentException">The game isn't DEG Mods'.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="since"/> is more than 28 days ago.</exception>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        string key = GameKey(game);
        if (_timeProvider.GetUtcNow() - since > UpdateReach + ReachGrace)
        {
            throw new ArgumentOutOfRangeException(nameof(since), "ModShelf reads DEG Mods' changes of the last 28 days only.");
        }

        IReadOnlyList<DegMod> changed = await _index.ChangedSinceAsync(since, cancellationToken).ConfigureAwait(false);
        return
        [
            .. changed
                .Where(mod => ModEvents.GameKey(mod.Game) == key && mod.UpdatedAt >= since)
                .Select(mod => new ModUpdate(ModOf(mod), mod.UpdatedAt, filesUpdatedAt: null, ModPage(mod))),
        ];
    }
}
