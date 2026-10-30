using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The About part's log messages.
/// </summary>
public sealed partial class AboutViewModel
{
    [LoggerMessage(EventId = 924, Level = LogLevel.Error, Message = "Opening the log folder failed")]
    private static partial void LogOpenLogFolderFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 925, Level = LogLevel.Error, Message = "Copying the diagnostics report failed")]
    private static partial void LogCopyDiagnosticsFailed(ILogger logger, Exception exception);
}
