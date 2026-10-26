using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IUpdateFeed"/> half: a game's mods updated in the last day, week or month, from the v1 API in one
/// request.
/// </summary>
public sealed partial class NexusSource : IUpdateFeed
{
    // The API's longest period is a month, and the shortest month has 28 days.
    private static readonly TimeSpan UpdateReach = TimeSpan.FromDays(28);

    // A since the caller read from its own clock a moment before this source reads it is still taken.
    private static readonly TimeSpan ReachGrace = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>
    /// 28 days: the API lists the mods updated in the last day, week or month, and the shortest month has 28.
    /// </summary>
    public TimeSpan Reach => UpdateReach;

    /// <summary>
    /// Asks <c>updated.json</c> for the shortest period that covers <paramref name="since"/>, a day, a week or a month,
    /// and keeps the mods active since then.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't Nexus Mods'.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="since"/> is more than 28 days ago.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused; with 404 when there's no such game.</exception>
    public async Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        string domain = NexusLink.ForGame(game);
        TimeSpan back = _timeProvider.GetUtcNow() - since;
        if (back > UpdateReach + ReachGrace)
        {
            throw new ArgumentOutOfRangeException(
                nameof(since),
                "Nexus Mods lists the mods updated in the last 28 days only.");
        }

        string period = back <= Day ? "1d" : back <= Week ? "1w" : "1m";
        NexusUpdatedMod[] updated = await _api.GetAsync<NexusUpdatedMod[]>($"v1/games/{domain}/mods/updated.json?period={period}", cancellationToken)
            .ConfigureAwait(false);

        // The period can reach further back than asked, and an entry without an activity time can't say it's recent.
        return
        [
            .. updated
                .Where(mod => mod is not null && mod.ModId > 0 && mod.LatestModActivity is { } activity && FromUnixSeconds(activity) >= since)
                .Select(mod => ToModUpdate(game, domain, mod)),
        ];
    }

    private static ModUpdate ToModUpdate(GameRef game, string domain, NexusUpdatedMod mod)
    {
        string id = mod.ModId.ToString(CultureInfo.InvariantCulture);
        return new ModUpdate(
            new ModRef(game, id),
            FromUnixSeconds(mod.LatestModActivity!.Value),
            mod.LatestFileUpdate is > 0 and { } files ? FromUnixSeconds(files) : null,
            new Uri(Website, $"{domain}/mods/{id}"));
    }
}
