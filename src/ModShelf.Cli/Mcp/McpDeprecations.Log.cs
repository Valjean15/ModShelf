using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The deprecated arguments' log message.
/// </summary>
internal sealed partial class McpDeprecations
{
    // The arguments aren't logged: an nxm:// link carries the site's key.
    [LoggerMessage(EventId = 1415, Level = LogLevel.Warning, Message = "{Code} {Tool} called with deprecated '{Argument}'; use '{Use}' (client: {Client})")]
    private static partial void LogDeprecatedArgument(ILogger logger, string code, string tool, string argument, string use, string client);
}
