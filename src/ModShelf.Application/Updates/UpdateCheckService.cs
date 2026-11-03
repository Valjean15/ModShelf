using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods for updates in the background, every <see cref="AppSettings.UpdateInterval"/>: first when
/// the oldest check is that old, or at once when a watched mod was never checked, then that long after each check. A
/// game with an interval of its own (<see cref="GameSettings.UpdateInterval"/>) is checked on its own, as often as that
/// says, from its own oldest check; a game whose updates aren't checked (<see cref="GameSettings.CheckUpdates"/>)
/// isn't. A check that finds new updates shows a notice of those of games that ask for one
/// (<see cref="AppSettings.NotifiesFor"/>).
/// </summary>
/// <remarks>
/// Nothing but stopping ends the checks: whatever fails is logged, and checked again an interval later. No check is due
/// later than an interval from now, since a later time means the clock has moved back. A changed interval counts within
/// <see cref="UpdateInterval.Minimum"/>.
/// </remarks>
internal sealed partial class UpdateCheckService : BackgroundService
{
    // The longest the service waits before reading the interval again: the shortest interval there is.
    private static readonly TimeSpan WaitSlice = UpdateInterval.Minimum;

    private readonly IUpdateChecker _checker;
    private readonly ISettingsService _settings;
    private readonly ILibraryRepository _library;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(
        IUpdateChecker checker,
        ISettingsService settings,
        ILibraryRepository library,
        INotificationService notifications,
        TimeProvider timeProvider,
        ILogger<UpdateCheckService> logger)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _checker = checker;
        _settings = settings;
        _library = library;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            DateTimeOffset? lastCheck = null;
            Dictionary<GameRef, DateTimeOffset> ownChecks = [];
            while (true)
            {
                // The interval counts from the last check; before the first, from the library's oldest. A time later
                // than now means the clock has moved back, so it counts from now instead.
                DateTimeOffset now = _timeProvider.GetUtcNow();
                DateTimeOffset? from = lastCheck ?? await FirstFromAsync(stoppingToken).ConfigureAwait(false);
                if (from is { } since)
                {
                    await WaitAsync(since < now ? since : now, ownChecks, stoppingToken).ConfigureAwait(false);
                }

                lastCheck = _timeProvider.GetUtcNow();
                UpdateCheck? check;
                try
                {
                    check = await CheckEveryGameAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Such as a request that timed out.
                    LogCheckFailed(_logger, exception);
                    continue;
                }

                if (check is not null)
                {
                    await NotifyAsync(check, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>
    /// Whether a game has an interval of its own, so it's checked apart from the others.
    /// </summary>
    private static bool HasOwnIntervals(AppSettings settings) => settings.Games.Values.Any(game => game.UpdateInterval is not null);

    /// <summary>
    /// Whether <paramref name="game"/> is checked with every game: its updates are checked, on every game's interval.
    /// </summary>
    private static bool FollowsEveryGame(AppSettings? settings, GameRef game)
        => settings is null || (settings.ChecksUpdatesOf(game.Key) && settings.GameFor(game.Key).UpdateInterval is null);

    /// <summary>
    /// Checks the games checked on every game's interval: every game whose updates are checked, unless one has an
    /// interval of its own. Null when there's no such game to check.
    /// </summary>
    private async Task<UpdateCheck?> CheckEveryGameAsync(CancellationToken cancellationToken)
    {
        AppSettings? settings = await SettingsOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null || !HasOwnIntervals(settings))
        {
            return await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        }

        GameRef[] games =
        [
            .. (await _library.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(mod => mod.IsWatched && FollowsEveryGame(settings, mod.Mod.Game))
                .Select(mod => mod.Mod.Game)
                .Distinct(),
        ];
        return games.Length == 0 ? null : await _checker.CheckAsync(games, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until an interval after <paramref name="since"/>, a slice at a time, reading the interval again after each:
    /// so a shorter one chosen meanwhile counts within minutes, rather than after the wait the longer one set. Meanwhile,
    /// games with intervals of their own are checked when theirs are due.
    /// </summary>
    private async Task WaitAsync(DateTimeOffset since, Dictionary<GameRef, DateTimeOffset> ownChecks, CancellationToken stoppingToken)
    {
        while (true)
        {
            TimeSpan interval = await IntervalAsync(stoppingToken).ConfigureAwait(false);
            TimeSpan wait = since + interval - _timeProvider.GetUtcNow();
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            if (await CheckOwnIntervalsAsync(ownChecks, stoppingToken).ConfigureAwait(false) is { } next)
            {
                TimeSpan untilNext = next - _timeProvider.GetUtcNow();
                wait = untilNext < wait ? untilNext : wait;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait < WaitSlice ? wait : WaitSlice, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Checks the games with intervals of their own whose checks are due: those an interval past their last check this
    /// run, or before it, past their oldest mod's, or at once when one of their mods was never checked.
    /// </summary>
    /// <returns>When the next of their checks is due; null when no game has an interval of its own.</returns>
    private async Task<DateTimeOffset?> CheckOwnIntervalsAsync(Dictionary<GameRef, DateTimeOffset> ownChecks, CancellationToken stoppingToken)
    {
        AppSettings? settings = await SettingsOrNullAsync(stoppingToken).ConfigureAwait(false);
        if (settings is null || !HasOwnIntervals(settings))
        {
            ownChecks.Clear();
            return null;
        }

        IGrouping<GameRef, LibraryMod>[] games;
        try
        {
            games =
            [
                .. (await _library.ListAsync(stoppingToken).ConfigureAwait(false))
                    .Where(mod => mod.IsWatched)
                    .GroupBy(mod => mod.Mod.Game)
                    .Where(game => settings.ChecksUpdatesOf(game.Key.Key) && settings.GameFor(game.Key.Key).UpdateInterval is not null),
            ];
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogCheckFailed(_logger, exception);
            return null;
        }

        foreach (GameRef gone in ownChecks.Keys.Where(game => games.All(own => own.Key != game)).ToArray())
        {
            ownChecks.Remove(gone);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset? next = null;
        List<GameRef> due = [];
        foreach (IGrouping<GameRef, LibraryMod> game in games)
        {
            DateTimeOffset? last = ownChecks.TryGetValue(game.Key, out DateTimeOffset checkedAt) ? checkedAt
                : game.Any(mod => mod.CheckedAt is null) ? null
                : game.Min(mod => mod.CheckedAt!.Value);
            DateTimeOffset dueAt = last is { } from ? (from < now ? from : now) + settings.IntervalFor(game.Key.Key) : now;
            if (dueAt <= now)
            {
                due.Add(game.Key);
                ownChecks[game.Key] = now;
                dueAt = now + settings.IntervalFor(game.Key.Key);
            }

            next = next is { } earlier && earlier < dueAt ? earlier : dueAt;
        }

        if (due.Count > 0)
        {
            try
            {
                UpdateCheck check = await _checker.CheckAsync(due, stoppingToken).ConfigureAwait(false);
                await NotifyAsync(check, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogCheckFailed(_logger, exception);
            }
        }

        return next;
    }

    /// <summary>
    /// The interval in the settings; or the default when they can't be read, which is logged.
    /// </summary>
    private async Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).UpdateInterval ?? UpdateInterval.Default;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCantReadInterval(_logger, exception);
            return UpdateInterval.Default;
        }
    }

    /// <summary>
    /// The settings; null when they can't be read, which reading the interval logs.
    /// </summary>
    private async Task<AppSettings?> SettingsOrNullAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows a notice of the updates <paramref name="check"/> found that no check had found before, of the games that
    /// ask for one, unless there are none. Settings that can't be read mean a notice. A notice that can't be put together
    /// or shown is logged.
    /// </summary>
    private async Task NotifyAsync(UpdateCheck check, CancellationToken cancellationToken)
    {
        if (check.Found.Count == 0)
        {
            return;
        }

        try
        {
            // Settings that can't be read: the interval's read already said so.
            AppSettings? settings = await SettingsOrNullAsync(cancellationToken).ConfigureAwait(false);
            LibraryUpdate[] found = [.. check.Found.Where(update => settings?.NotifiesFor(update.File.Mod.Game.Key) ?? true)];
            if (found.Length > 0)
            {
                IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
                await _notifications.ShowAsync(UpdateNotices.For(found, library), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCantNotify(_logger, exception, check.Found.Count);
        }
    }

    /// <summary>
    /// When the interval before the first check counts from: the oldest check of the mods checked on every game's
    /// interval, or none, for a check at once, when one was never checked. When the library can't be read, which is
    /// logged, it counts from now.
    /// </summary>
    private async Task<DateTimeOffset?> FirstFromAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppSettings? settings = await SettingsOrNullAsync(cancellationToken).ConfigureAwait(false);
            LibraryMod[] watched =
            [
                .. (await _library.ListAsync(cancellationToken).ConfigureAwait(false))
                    .Where(mod => mod.IsWatched && FollowsEveryGame(settings, mod.Mod.Game)),
            ];
            return watched.Length == 0 || watched.Any(mod => mod.CheckedAt is null)
                ? null
                : watched.Min(mod => mod.CheckedAt!.Value);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCheckFailed(_logger, exception);
            return _timeProvider.GetUtcNow();
        }
    }
}
