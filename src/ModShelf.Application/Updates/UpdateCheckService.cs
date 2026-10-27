using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods for updates in the background, every <see cref="AppSettings.UpdateInterval"/>: first when
/// the oldest check is that old, or at once when a watched mod was never checked, then that long after each check.
/// </summary>
internal sealed partial class UpdateCheckService : BackgroundService
{
    private readonly IUpdateChecker _checker;
    private readonly ISettingsService _settings;
    private readonly ILibraryRepository _library;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(
        IUpdateChecker checker,
        ISettingsService settings,
        ILibraryRepository library,
        TimeProvider timeProvider,
        ILogger<UpdateCheckService> logger)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _checker = checker;
        _settings = settings;
        _library = library;
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
                try
                {
                    await _checker.CheckAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCheckFailed(_logger, exception);
                }
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
