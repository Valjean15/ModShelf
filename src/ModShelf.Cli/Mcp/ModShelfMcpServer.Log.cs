using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP server's log messages. What the agent asks is never logged: it can carry links.
/// </summary>
internal static partial class ModShelfMcpServer
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "The MCP server started, with {Tools} tools")]
    private static partial void LogStarted(ILogger logger, int tools);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "The MCP server ended: its client closed it")]
    private static partial void LogEnded(ILogger logger);
}
