using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.FirstRun;

/// <summary>
/// The first-run setup's log messages.
/// </summary>
public sealed partial class FirstRunViewModel
{
    [LoggerMessage(EventId = 930, Level = LogLevel.Information, Message = "The first-run setup is shown")]
    private static partial void LogShown(ILogger logger);

    [LoggerMessage(EventId = 931, Level = LogLevel.Information, Message = "The first-run setup was {How}")]
    private static partial void LogDone(ILogger logger, string how);

    [LoggerMessage(EventId = 932, Level = LogLevel.Information, Message = "The library is in use already, so it's marked set up without the first-run setup")]
    private static partial void LogInUseAlready(ILogger logger);

    [LoggerMessage(EventId = 933, Level = LogLevel.Error, Message = "Marking the first-run setup done failed")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 934, Level = LogLevel.Error, Message = "Telling whether the library is new, for the first-run setup, failed")]
    private static partial void LogCantTell(ILogger logger, Exception exception);
}
