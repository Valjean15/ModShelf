using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP server's queue's log messages.
/// </summary>
internal sealed partial class McpQueue
{
    [LoggerMessage(EventId = 1410, Level = LogLevel.Information, Message = "No desktop app runs this library's downloads, so the MCP server runs them until none is under way")]
    private static partial void LogRunsHere(ILogger logger);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Information, Message = "No download is under way, so the MCP server stopped running the downloads")]
    private static partial void LogDone(ILogger logger);

    [LoggerMessage(EventId = 1412, Level = LogLevel.Warning, Message = "Another process that takes no requests runs this library's downloads")]
    private static partial void LogBusyElsewhere(ILogger logger);

    [LoggerMessage(EventId = 1413, Level = LogLevel.Warning, Message = "Couldn't look whether a download is under way; looking again later")]
    private static partial void LogCantLook(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1414, Level = LogLevel.Warning, Message = "The MCP server's downloads didn't stop cleanly")]
    private static partial void LogCantStop(ILogger logger, Exception exception);
}
