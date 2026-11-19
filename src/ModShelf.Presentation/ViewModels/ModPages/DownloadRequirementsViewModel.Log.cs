using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// The requirements dialog's log messages.
/// </summary>
public sealed partial class DownloadRequirementsViewModel
{
    [LoggerMessage(EventId = 986, Level = LogLevel.Error, Message = "Reading what mod {ModId} of {Game} needs failed")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string modId, string game);
}
