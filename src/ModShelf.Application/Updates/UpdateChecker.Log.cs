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
}
