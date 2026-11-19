using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// The log messages of Show contents.
/// </summary>
public sealed partial class FileContentsViewModel
{
    [LoggerMessage(EventId = 991, Level = LogLevel.Warning, Message = "Couldn't list what an archive holds")]
    private static partial void LogListFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 992, Level = LogLevel.Warning, Message = "Couldn't read a text file in an archive")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);
}
