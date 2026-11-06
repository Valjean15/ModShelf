using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.QuickSearch;

/// <summary>
/// Quick search's log messages.
/// </summary>
public sealed partial class QuickSearchViewModel
{
    [LoggerMessage(EventId = 955, Level = LogLevel.Error, Message = "Quick search couldn't read the library or the sites' games")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 956, Level = LogLevel.Warning, Message = "Couldn't remember quick search's search")]
    private static partial void LogNotRemembered(ILogger logger, Exception exception);
}
