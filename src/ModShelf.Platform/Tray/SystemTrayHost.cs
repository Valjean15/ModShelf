using Microsoft.Extensions.Logging;

using ModShelf.Application.Desktop.Ports;

namespace ModShelf.Platform.Tray;

/// <summary>
/// A tray always on Windows and macOS. On Linux, one while a tray host owns <c>org.kde.StatusNotifierWatcher</c> on the
/// session bus, which Avalonia's tray icons need; it's asked each time, since a tray host can start or stop. Elsewhere,
/// none.
/// </summary>
internal sealed partial class SystemTrayHost : ITrayHost
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly Func<bool> _isWatcherRunning;
    private readonly ILogger _logger;

    public SystemTrayHost(ILogger logger)
        : this(IsWatcherRunning, logger)
    {
    }

    /// <param name="isWatcherRunning">On Linux, asks the session bus for a tray host; tests replace it.</param>
    public SystemTrayHost(Func<bool> isWatcherRunning, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(isWatcherRunning);
        ArgumentNullException.ThrowIfNull(logger);

        _isWatcherRunning = isWatcherRunning;
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return Task.FromResult(true);
        }

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(false);
        }

        // The bus answers within its timeout; off the caller's thread, which is often the UI's.
        return Task.Run(AskTheBus, cancellationToken);
    }

    private static bool IsWatcherRunning()
        => OperatingSystem.IsLinux() && StatusNotifierInterop.NameHasOwner(StatusNotifierInterop.WatcherName, Patience);

    private bool AskTheBus()
    {
        try
        {
            if (_isWatcherRunning())
            {
                return true;
            }

            LogNoTrayHost(_logger);
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Such as no session bus, or no GIO: no tray icon could show either.
            LogCantAsk(_logger, exception.Message);
            return false;
        }
    }
}
