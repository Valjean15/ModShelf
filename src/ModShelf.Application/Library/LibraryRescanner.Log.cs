using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The rescan's log messages.
/// </summary>
internal sealed partial class LibraryRescanner
{
    [LoggerMessage(EventId = 621, Level = LogLevel.Information, Message = "Rescanned {Mods} mods: {Added} files added, {Hashed} hashed, {Missing} missing, {Unmatched} not matched, {Failures} mods not matched")]
    private static partial void LogRescanned(ILogger logger, int mods, int added, int hashed, int missing, int unmatched, int failures);

    [LoggerMessage(EventId = 622, Level = LogLevel.Information, Message = "Added {FileName}, found on disk, to the library as a file of mod {ModId} of {Game}")]
    private static partial void LogAdded(ILogger logger, string fileName, string modId, string game);

    [LoggerMessage(EventId = 623, Level = LogLevel.Warning, Message = "Couldn't list {Folder} to rescan it")]
    private static partial void LogCantList(ILogger logger, Exception exception, string folder);

    [LoggerMessage(EventId = 624, Level = LogLevel.Warning, Message = "Couldn't ask the site about mod {ModId} of {Game} to rescan it")]
    private static partial void LogCantAsk(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 625, Level = LogLevel.Warning, Message = "Couldn't read {FileName} for its SHA-256 while rescanning")]
    private static partial void LogCantHash(ILogger logger, Exception exception, string fileName);
}
