using System.Diagnostics;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Shows notices on macOS through <c>osascript</c>'s <c>display notification</c>, which works for an app without a
/// bundle. macOS names them after Script Editor until ModShelf is a signed app bundle (Phase 6).
/// </summary>
/// <remarks>
/// The title and message reach the script as its arguments, never as part of its text, so nothing in them is ever run.
/// </remarks>
internal sealed class OsascriptNotificationService : INotificationService
{
    private const string Osascript = "/usr/bin/osascript";

    /// <summary>
    /// How long <c>osascript</c> may take before it's stopped; it normally takes a fraction of a second.
    /// </summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly string[] Script =
    [
        "on run argv",
        "display notification (item 2 of argv) with title (item 1 of argv)",
        "end run",
    ];

    private readonly Func<ProcessStartInfo, CancellationToken, Task<ProcessResult>> _run;
    private readonly ILogger _logger;

    public OsascriptNotificationService(ILogger logger)
        : this((info, cancellationToken) => ProcessResult.RunAsync(info, Timeout, cancellationToken), logger)
    {
    }

    /// <param name="run">Runs <c>osascript</c>; tests replace it.</param>
    internal OsascriptNotificationService(Func<ProcessStartInfo, CancellationToken, Task<ProcessResult>> run, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(logger);

        _run = run;
        _logger = logger;
    }

    public async Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            ProcessResult result = await _run(StartInfoFor(notification), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"osascript stopped with exit code {result.ExitCode}: {result.Errors.Trim()}");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotificationServices.LogCantShow(_logger, exception, notification.Title);
        }
    }

    /// <summary>
    /// <c>osascript</c> with the script, then the title and the message as its arguments. The title goes first, since
    /// <c>osascript</c> reads options until its first argument and ModShelf's titles never start with a hyphen.
    /// </summary>
    internal static ProcessStartInfo StartInfoFor(Notification notification)
    {
        ProcessStartInfo info = new(Osascript) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string line in Script)
        {
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(line);
        }

        info.ArgumentList.Add(notification.Title);
        info.ArgumentList.Add(notification.Message);
        return info;
    }
}
