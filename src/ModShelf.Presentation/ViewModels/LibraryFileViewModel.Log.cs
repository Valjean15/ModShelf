using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A library file's log messages, logged as the Library screen's.
/// </summary>
public sealed partial class LibraryFileViewModel
{
    [LoggerMessage(EventId = 906, Level = LogLevel.Error, Message = "Deleting file {FileId} of mod {ModId} failed")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string fileId, string modId);
}
