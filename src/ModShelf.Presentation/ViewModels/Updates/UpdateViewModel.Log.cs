using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Updates;

/// <summary>
/// An update's log messages, logged as the Updates screen's.
/// </summary>
public sealed partial class UpdateViewModel
{
    [LoggerMessage(EventId = 908, Level = LogLevel.Error, Message = "{Action} of update {FileId} of mod {ModId} failed")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, UpdateAction action, string fileId, string modId);
}
