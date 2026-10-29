using Microsoft.Extensions.Logging;

namespace ModShelf.Desktop;

/// <summary>
/// The desktop app's startup log messages.
/// </summary>
internal static partial class Program
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Critical, Message = "ModShelf couldn't start")]
    private static partial void LogCantStart(ILogger logger, Exception exception);
}
