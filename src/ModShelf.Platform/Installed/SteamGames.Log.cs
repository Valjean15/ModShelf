using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Installed;

internal sealed partial class SteamGames
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Steam isn't on this computer, so it has no installed games")]
    private static partial void LogNotFound(ILogger logger);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Information, Message = "Steam has {Games} games installed in {Libraries} library folders")]
    private static partial void LogRead(ILogger logger, int games, int libraries);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Couldn't read Steam's list of library folders, so only its own folder's games are read")]
    private static partial void LogLibrariesUnread(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "Couldn't read one of Steam's library folders, so its games are left out")]
    private static partial void LogLibraryUnread(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Warning, Message = "Couldn't read Steam's manifest of app {App}, so it's left out")]
    private static partial void LogManifestUnread(ILogger logger, string app, Exception exception);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Warning, Message = "Steam's manifest of app {App} doesn't say its ID, name or folder, so it's left out")]
    private static partial void LogManifestIncomplete(ILogger logger, string app);

    [LoggerMessage(EventId = 2206, Level = LogLevel.Debug, Message = "Steam app {App} isn't fully installed, so it's left out")]
    private static partial void LogNotInstalled(ILogger logger, string app);
}
