using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A browsed file's log messages, logged as the Browse screen's.
/// </summary>
public sealed partial class ModFileViewModel
{
    [LoggerMessage(EventId = 913, Level = LogLevel.Error, Message = "{Action} of file {FileId} of mod {ModId} failed")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, FileAction action, string fileId, string modId);
}
