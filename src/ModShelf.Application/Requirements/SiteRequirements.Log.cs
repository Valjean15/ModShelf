using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Requirements;

/// <summary>
/// Requirements' log messages, which name mods by their IDs alone.
/// </summary>
internal sealed partial class SiteRequirements
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Warning, Message = "Couldn't read the requirements of mod {ModId} of {Game}, so what it needs isn't listed")]
    private static partial void LogCantReadDeeper(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Warning, Message = "Couldn't read the files of mod {ModId} of {Game}, which a mod needs")]
    private static partial void LogCantReadFiles(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Warning, Message = "Couldn't queue requirement {Requirement} of {Game}")]
    private static partial void LogCantQueue(ILogger logger, Exception exception, string requirement, string game);
}
