using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The Links from websites part's log messages.
/// </summary>
public sealed partial class LinksViewModel
{
    [LoggerMessage(EventId = 926, Level = LogLevel.Error, Message = "Reading which app opens the sites' links failed")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 927, Level = LogLevel.Error, Message = "Making ModShelf open a site's links failed")]
    private static partial void LogTakeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 928, Level = LogLevel.Error, Message = "Giving a site's links back failed")]
    private static partial void LogGiveBackFailed(ILogger logger, Exception exception);
}
