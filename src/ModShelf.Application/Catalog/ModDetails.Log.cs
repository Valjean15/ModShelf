using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Catalog;

/// <summary>
/// The mod details' log messages.
/// </summary>
internal sealed partial class ModDetails
{
    [LoggerMessage(EventId = 1550, Level = LogLevel.Information, Message = "Mod {ModId} of {Game} on {Source} couldn't be read from its site, so its saved details, if any, are shown: {Reason}")]
    private static partial void LogCantRead(ILogger logger, string modId, string game, string source, string reason);

    [LoggerMessage(EventId = 1551, Level = LogLevel.Warning, Message = "The saved details of mod {ModId} of {Game} on {Source} can't be read, so they're read from its site again: {Reason}")]
    private static partial void LogSavedUnreadable(ILogger logger, string modId, string game, string source, string reason);
}
