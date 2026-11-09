using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Diagnostics;

/// <summary>
/// The line each process starts its logs with.
/// </summary>
internal sealed partial class ProcessStartLog
{
    [LoggerMessage(EventId = 1307, Level = LogLevel.Information, Message = "ModShelf {Version}: {Runs}, process {ProcessId}, started by {Parent} (process {ParentId})")]
    private static partial void LogStarted(ILogger logger, string version, string runs, int processId, string parent, int parentId);

    [LoggerMessage(EventId = 1308, Level = LogLevel.Information, Message = "ModShelf {Version}: {Runs}, process {ProcessId}; the system doesn't say which process started it")]
    private static partial void LogStartedByUnknown(ILogger logger, string version, string runs, int processId);
}
