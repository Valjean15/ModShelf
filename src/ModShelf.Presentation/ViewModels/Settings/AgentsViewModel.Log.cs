using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Settings › Agents' log messages.
/// </summary>
public sealed partial class AgentsViewModel
{
    [LoggerMessage(EventId = 960, Level = LogLevel.Warning, Message = "Couldn't save whether agents may use ModShelf, or their port")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 961, Level = LogLevel.Error, Message = "Couldn't serve the MCP server's address, or stop serving it")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 962, Level = LogLevel.Error, Message = "Couldn't copy the prompt for an agent")]
    private static partial void LogCopyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 963, Level = LogLevel.Information, Message = "Added ModShelf's MCP server to Claude Desktop's configuration")]
    private static partial void LogAddedToClaudeDesktop(ILogger logger);

    [LoggerMessage(EventId = 964, Level = LogLevel.Error, Message = "Couldn't add ModShelf's MCP server to Claude Desktop")]
    private static partial void LogAddFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 965, Level = LogLevel.Error, Message = "Couldn't look whether Claude Desktop has ModShelf's MCP server")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);
}
