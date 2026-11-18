using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// The mod lists' log messages.
/// </summary>
internal sealed partial class ModLists
{
    [LoggerMessage(EventId = 2300, Level = LogLevel.Information, Message = "Imported a list of {Mods} mods: {Added} added, {Queued} files queued, {Problems} not done")]
    private static partial void LogImported(ILogger logger, int mods, int added, int queued, int problems);

    [LoggerMessage(EventId = 2301, Level = LogLevel.Warning, Message = "Couldn't import mod {ModId} of {Game} from a list")]
    private static partial void LogImportFailed(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 2302, Level = LogLevel.Information, Message = "Read collection {Name} of {Game} as a list: {Files} files, {Elsewhere} things from elsewhere, {Unavailable} files no longer offered")]
    private static partial void LogCollectionRead(ILogger logger, string name, GameRef game, int files, int elsewhere, int unavailable);
}
