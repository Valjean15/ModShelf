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

    [LoggerMessage(EventId = 1422, Level = LogLevel.Information, Message = "The MCP server is at {Address}")]
    private static partial void LogServing(ILogger logger, Uri address);

    [LoggerMessage(EventId = 1423, Level = LogLevel.Information, Message = "The MCP server's address is no longer served")]
    private static partial void LogNoLongerServing(ILogger logger);

    [LoggerMessage(EventId = 1424, Level = LogLevel.Warning, Message = "Refused a request to the MCP server's address that didn't come from this computer's apps")]
    private static partial void LogNotLocal(ILogger logger);

    [LoggerMessage(EventId = 1425, Level = LogLevel.Warning, Message = "Port {Port} is in use by another app, so the MCP server's address can't be served")]
    private static partial void LogPortInUse(ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 1426, Level = LogLevel.Information, Message = "Process {Parent}, which the MCP server's address ran for, ended")]
    private static partial void LogParentEnded(ILogger logger, int parent);
}
