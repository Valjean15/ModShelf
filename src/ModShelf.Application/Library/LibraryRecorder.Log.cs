using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The library recorder's log messages.
/// </summary>
internal sealed partial class LibraryRecorder
{
    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "Couldn't add the finished download {FileName} to the library")]
    private static partial void LogCantRecord(ILogger logger, Exception exception, string fileName);
}
