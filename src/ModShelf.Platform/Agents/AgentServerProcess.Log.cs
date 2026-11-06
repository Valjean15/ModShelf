using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Agents;

/// <summary>
/// The MCP server's address's log messages.
/// </summary>
internal sealed partial class AgentServerProcess
{
    [LoggerMessage(EventId = 1080, Level = LogLevel.Information, Message = "The MCP server's address is served at port {Port}")]
    private static partial void LogServing(ILogger logger, int port);

    [LoggerMessage(EventId = 1081, Level = LogLevel.Warning, Message = "The command line, which serves the MCP server's address, isn't beside the app")]
    private static partial void LogNoCommandLine(ILogger logger);

    [LoggerMessage(EventId = 1082, Level = LogLevel.Warning, Message = "The MCP server's address at port {Port} ended, with exit code {ExitCode}")]
    private static partial void LogEnded(ILogger logger, int port, int exitCode);

    [LoggerMessage(EventId = 1083, Level = LogLevel.Warning, Message = "The MCP server's address at port {Port} didn't start in time")]
    private static partial void LogSlow(ILogger logger, int port);
}
