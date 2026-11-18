using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Updates;

/// <summary>
/// The update checker's log messages.
/// </summary>
internal sealed partial class UpdateChecker
{
    [LoggerMessage(EventId = 700, Level = LogLevel.Information, Message = "Checked {Checked} watched mods for updates with {Requests} requests: {Found} new updates, {Failures} failures")]
    private static partial void LogChecked(ILogger logger, int @checked, int requests, int found, int failures);

    [LoggerMessage(EventId = 701, Level = LogLevel.Warning, Message = "Couldn't ask {Source} which mods of {Game} changed")]
    private static partial void LogCantCheckGame(ILogger logger, Exception exception, string game, string source);

    [LoggerMessage(EventId = 702, Level = LogLevel.Warning, Message = "Couldn't check mod {ModId} of {Game} on {Source} for updates")]
    private static partial void LogCantCheckMod(ILogger logger, Exception exception, string modId, string game, string source);

    [LoggerMessage(EventId = 706, Level = LogLevel.Information, Message = "{Source}'s feed found the question about {Game} beyond its reach, so its mods are asked about one by one")]
    private static partial void LogFeedOutOfReach(ILogger logger, Exception exception, string game, string source);

    [LoggerMessage(EventId = 707, Level = LogLevel.Warning, Message = "Couldn't read which games' updates to check, so every game's are checked")]
    private static partial void LogSettingsUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 708, Level = LogLevel.Warning, Message = "Mod {ModId} of {Game} is gone from {Source}: hidden or removed there, so the library's copies of its files may be the only ones left")]
    private static partial void LogLeftSite(ILogger logger, string modId, string game, string source);

    [LoggerMessage(EventId = 709, Level = LogLevel.Information, Message = "Mod {ModId} of {Game} is back on {Source}")]
    private static partial void LogBackOnSite(ILogger logger, string modId, string game, string source);
}
