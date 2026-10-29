using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods for updates in the background, every <see cref="AppSettings.UpdateInterval"/>: first when
/// the oldest check is that old, or at once when a watched mod was never checked, then that long after each check. A
/// check that finds new updates shows a notice, unless <see cref="AppSettings.NotifyOfUpdates"/> is false.
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
            while (true)
            {
                // The interval counts from the last check; before the first, from the library's oldest. A time later
                // than now means the clock has moved back, so it counts from now instead.
                DateTimeOffset now = _timeProvider.GetUtcNow();
                DateTimeOffset? from = lastCheck ?? await FirstFromAsync(stoppingToken).ConfigureAwait(false);
                if (from is { } since)
                {
                    await WaitAsync(since < now ? since : now, stoppingToken).ConfigureAwait(false);
                }

                lastCheck = _timeProvider.GetUtcNow();
                UpdateCheck check;
                try
                {
                    check = await _checker.CheckAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Such as a request that timed out.
                    LogCheckFailed(_logger, exception);
                    continue;
                }

                await NotifyAsync(check, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>
    /// Waits until an interval after <paramref name="since"/>, a slice at a time, reading the interval again after each:
    /// so a shorter one chosen meanwhile counts within minutes, rather than after the wait the longer one set.
    /// </summary>
    private async Task WaitAsync(DateTimeOffset since, CancellationToken stoppingToken)
    {
        while (true)
        {
            TimeSpan interval = await IntervalAsync(stoppingToken).ConfigureAwait(false);
            TimeSpan wait = since + interval - _timeProvider.GetUtcNow();
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(wait < WaitSlice ? wait : WaitSlice, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
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
    /// Shows a notice of the updates <paramref name="check"/> found that no check had found before, unless there are
    /// none, or the settings say not to. Settings that can't be read mean a notice. A notice that can't be put together
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
            bool notify;
            try
            {
                notify = (await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).NotifyOfUpdates ?? true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The interval's read already said the settings can't be read.
                notify = true;
            }

            if (notify)
            {
                IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
                await _notifications.ShowAsync(UpdateNotices.For(check.Found, library), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCantNotify(_logger, exception, check.Found.Count);
        }
    }

    /// <summary>
    /// When the interval before the first check counts from: the oldest check, or none, for a check at once, when a
    /// watched mod was never checked. When the library can't be read, which is logged, it counts from now.
    /// </summary>
    private async Task<DateTimeOffset?> FirstFromAsync(CancellationToken cancellationToken)
    {
        try
        {
            LibraryMod[] watched = [.. (await _library.ListAsync(cancellationToken).ConfigureAwait(false)).Where(mod => mod.IsWatched)];
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
