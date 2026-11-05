using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Diagnostics;

/// <summary>
/// The deprecated calls file's log messages.
/// </summary>
internal sealed partial class JsonDeprecatedCallStore
{
    [LoggerMessage(EventId = 1701, Level = LogLevel.Information, Message = "The deprecated calls counted in {Path} can't be read, so they're forgotten, and counted again from the next: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string path, string reason);
}
