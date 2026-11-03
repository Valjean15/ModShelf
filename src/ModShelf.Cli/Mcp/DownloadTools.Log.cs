using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The download tools' log messages.
/// </summary>
internal sealed partial class DownloadTools
{
    // The links aren't logged: an nxm:// link carries the site's key.
    [LoggerMessage(EventId = 1415, Level = LogLevel.Warning, Message = "{Code} download called with legacy 'link'; normalized to links=[{Count} link] (client: {Client})")]
    private static partial void LogLegacyLink(ILogger logger, string code, int count, string client);
}
