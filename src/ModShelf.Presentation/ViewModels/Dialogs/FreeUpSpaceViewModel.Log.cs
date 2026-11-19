using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// The Free up space dialog's log messages.
/// </summary>
public sealed partial class FreeUpSpaceViewModel
{
    [LoggerMessage(EventId = 987, Level = LogLevel.Error, Message = "Listing what could go from the library failed")]
    private static partial void LogListFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 988, Level = LogLevel.Error, Message = "Deleting the files chosen to free up space failed")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception);
}
