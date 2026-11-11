using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IUpdateFeed"/> half: the game's mods whose live file changed since a time, from its events, 100 a page,
/// a few pages for a game with many mods.
/// </summary>
public sealed partial class ModIoSource : IUpdateFeed
{
    /// <summary>
    /// The most pages of events read: past them, the mods are checked one by one.
    /// </summary>
    internal const int EventPages = 10;

    // mod.io keeps a game's events for years; reading a month of a busy game's takes a few requests.
    private static readonly TimeSpan UpdateReach = TimeSpan.FromDays(28);

    // A since the caller read from its own clock a moment before this source reads it is still taken.
    private static readonly TimeSpan ReachGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 28 days, as Nexus Mods' feed: a busy game's file changes of a month take a few pages.
    /// </summary>
    public TimeSpan Reach => UpdateReach;

    /// <returns>
    /// The game's mods whose live file changed since <paramref name="since"/>, each when it last did: mod.io's events
    /// say when a file changed, and ModShelf asks for no other.
    /// </returns>
    /// <exception cref="ArgumentException">The game isn't mod.io's.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="since"/> is more than 28 days ago, or more files changed since than ModShelf reads at once.
    /// </exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused; with 403 when the game is closed to apps.</exception>
    public async Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        long gameId = GameId(game);
        if (_timeProvider.GetUtcNow() - since > UpdateReach + ReachGrace)
        {
            throw new ArgumentOutOfRangeException(nameof(since), "ModShelf reads mod.io's file changes of the last 28 days only.");
        }

        // In whole seconds, as mod.io counts, from the second the time asked is in.
        string query = $"games/{Number(gameId)}/mods/events?event_type=MODFILE_CHANGED&date_added-min={Number(since.ToUnixTimeSeconds())}&_limit={PageSize}";
        List<ModIoEvent> events = [];
        for (int page = 0; ; page++)
        {
            if (page == EventPages)
            {
                throw new ArgumentOutOfRangeException(nameof(since), "More of the game's files changed on mod.io since then than ModShelf reads at once.");
            }

            string offset = page == 0 ? string.Empty : $"&_offset={Number(page * PageSize)}";
            ModIoPage<ModIoEvent> found = await _api.GetAsync<ModIoPage<ModIoEvent>>(GameApi(gameId, query + offset), cancellationToken).ConfigureAwait(false);
            if (page == 0 && found.ResultTotal > EventPages * PageSize)
            {
                LogTooManyChanges(_logger, found.ResultTotal, gameId, since);
                throw new ArgumentOutOfRangeException(nameof(since), "More of the game's files changed on mod.io since then than ModShelf reads at once.");
            }

            IReadOnlyList<ModIoEvent> data = found.Data ?? [];
            events.AddRange(data.Where(change => change is not null));
            if (data.Count < PageSize || events.Count >= found.ResultTotal)
            {
                break;
            }
        }

        return
        [
            .. events
                .Where(change => change is { ModId: > 0 } && ModIoMods.Time(change.DateAdded) >= since)
                .GroupBy(change => change.ModId)
                .Select(changes =>
                {
                    DateTimeOffset last = ModIoMods.Time(changes.Max(change => change.DateAdded))!.Value;
                    return new ModUpdate(new ModRef(game, Number(changes.Key)), last, filesUpdatedAt: last);
                }),
        ];
    }
}
