using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The source's log messages. A file's link is never logged: only its host.
/// </summary>
public sealed partial class DegModsSource
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "A DEG Mods mod its moderation hides was asked for; ModShelf doesn't show or download it")]
    private static partial void LogHidden(ILogger logger);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "DEG Mods' file {FileId} is on {Host}")]
    private static partial void LogFoundOn(ILogger logger, string fileId, string host);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "DEG Mods' file {FileId} is only on DEG Mods' own server, which gives files to its site alone: the mod's page opens")]
    private static partial void LogOnlyGated(ILogger logger, string fileId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "A DEG Mods file is on a page at {Host}, which opens in the browser")]
    private static partial void LogOnAPage(ILogger logger, string host);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "The file server {Host} couldn't be asked for a DEG Mods file")]
    private static partial void LogServerFailed(ILogger logger, Exception exception, string host);
}
