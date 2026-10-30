using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Diagnostics;

internal sealed partial class DiagnosticsReport
{
    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "The diagnostics report couldn't read its {Part}")]
    private static partial void LogPartUnreadable(ILogger logger, Exception exception, string part);
}
