using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.State;

/// <summary>
/// The UI state file's log messages.
/// </summary>
internal sealed partial class JsonUiStateStore
{
    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "The UI state in {Path} can't be read, so it's forgotten, and replaced when it's next saved: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string path, string reason);
}
