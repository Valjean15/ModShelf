using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The library's log messages.
/// </summary>
internal sealed partial class ModLibrary
{
    [LoggerMessage(EventId = 613, Level = LogLevel.Information, Message = "Deleted {FileName}, a file of mod {ModId} of {Game}, from disk and the library")]
    private static partial void LogDeletedFile(ILogger logger, string fileName, string modId, string game);

    [LoggerMessage(EventId = 619, Level = LogLevel.Information, Message = "Forgot mod {ModId} of {Game}: its {FileCount} files stay on disk, and {Cleared} of its completed downloads left the queue")]
    private static partial void LogForgot(ILogger logger, string modId, string game, int fileCount, int cleared);
}
