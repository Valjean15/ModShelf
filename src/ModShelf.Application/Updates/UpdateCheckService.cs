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
internal sealed partial class UpdateCheckService : BackgroundService
{
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
                TimeSpan interval = await IntervalAsync(stoppingToken).ConfigureAwait(false);
                DateTimeOffset due = lastCheck is { } last ? last + interval : await FirstDueAsync(interval, stoppingToken).ConfigureAwait(false);
                TimeSpan wait = due - _timeProvider.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _timeProvider, stoppingToken).ConfigureAwait(false);
                }

                lastCheck = _timeProvider.GetUtcNow();
                UpdateCheck check;
                try
                {
                    check = await _checker.CheckAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
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
    /// The interval in the settings; or the default when they can't be read, which is logged.
    /// </summary>
    private async Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).UpdateInterval ?? UpdateInterval.Default;
        }
        catch (InvalidDataException exception)
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
            catch (InvalidDataException)
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantNotify(_logger, exception, check.Found.Count);
        }
    }

    /// <summary>
    /// When the first check is due: now, when a watched mod was never checked; otherwise an interval after the oldest
    /// check.
    /// </summary>
    private async Task<DateTimeOffset> FirstDueAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            LibraryMod[] watched = [.. (await _library.ListAsync(cancellationToken).ConfigureAwait(false)).Where(mod => mod.IsWatched)];
            return watched.Length == 0 || watched.Any(mod => mod.CheckedAt is null)
                ? now
                : watched.Min(mod => mod.CheckedAt!.Value) + interval;
        }
        catch (InvalidDataException exception)
        {
            LogCheckFailed(_logger, exception);
            return now + interval;
        }
    }
}
