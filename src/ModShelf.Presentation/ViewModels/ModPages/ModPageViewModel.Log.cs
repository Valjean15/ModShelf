using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// A browsed mod's log messages, logged as the Browse screen's.
/// </summary>
public sealed partial class ModPageViewModel
{
    [LoggerMessage(EventId = 912, Level = LogLevel.Error, Message = "{Action} of mod {ModId} of {Game} failed")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, PageAction action, string modId, string game);

    [LoggerMessage(EventId = 914, Level = LogLevel.Information, Message = "Couldn't read the tracked mods on {Source}, so whether a mod is tracked isn't known: {Reason}")]
    private static partial void LogTrackedUnknown(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 981, Level = LogLevel.Information, Message = "Couldn't read the endorsements on {Source}, so whether a mod is endorsed isn't known: {Reason}")]
    private static partial void LogEndorsedUnknown(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 982, Level = LogLevel.Warning, Message = "Reading the endorsements on {Source} failed, so whether a mod is endorsed isn't known")]
    private static partial void LogEndorsedFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 915, Level = LogLevel.Warning, Message = "Reading the tracked mods on {Source} failed, so whether a mod is tracked isn't known")]
    private static partial void LogTrackedFailed(ILogger logger, Exception exception, string source);
}
