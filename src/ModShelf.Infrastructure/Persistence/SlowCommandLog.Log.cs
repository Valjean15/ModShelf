using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database's warnings of slow commands.
/// </summary>
internal sealed partial class SlowCommandLog
{
    [LoggerMessage(EventId = 205, Level = LogLevel.Warning, Message = "The library database took {Seconds:0.0} s for {Command}")]
    private static partial void LogSlow(ILogger logger, double seconds, string command);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning, Message = "The library database failed {Command} after {Seconds:0.0} s")]
    private static partial void LogFailedSlowly(ILogger logger, string command, double seconds);
}
