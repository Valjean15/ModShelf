using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// The archive import dialog's log messages.
/// </summary>
public sealed partial class ImportArchivesViewModel
{
    [LoggerMessage(EventId = 984, Level = LogLevel.Error, Message = "Identifying the archives failed")]
    private static partial void LogIdentifyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 985, Level = LogLevel.Error, Message = "Importing the archives failed")]
    private static partial void LogImportFailed(ILogger logger, Exception exception);
}
