using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// Agent access's log messages.
/// </summary>
internal static partial class AgentAccess
{
    [LoggerMessage(EventId = 1420, Level = LogLevel.Information, Message = "Refused a call of {Tool}: agents are off")]
    private static partial void LogRefused(ILogger logger, string? tool);

    [LoggerMessage(EventId = 1421, Level = LogLevel.Warning, Message = "Refused a call: the settings can't be read, so agents count as off")]
    private static partial void LogSettingsUnread(ILogger logger, Exception exception);
}
