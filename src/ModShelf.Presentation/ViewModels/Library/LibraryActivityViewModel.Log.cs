using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The recent activity's log messages.
/// </summary>
public sealed partial class LibraryActivityViewModel
{
    [LoggerMessage(EventId = 949, Level = LogLevel.Error, Message = "Reading the library's history failed")]
    private static partial void LogCantRead(ILogger logger, Exception exception);
}
