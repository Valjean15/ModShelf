using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.Services;

/// <summary>
/// The log message of finding which sites are in use.
/// </summary>
internal sealed partial class SitesInUse
{
    [LoggerMessage(EventId = 950, Level = LogLevel.Warning, Message = "Couldn't tell which sites are in use, so the screens don't show sites")]
    private static partial void LogUnknown(ILogger logger, Exception exception);
}
