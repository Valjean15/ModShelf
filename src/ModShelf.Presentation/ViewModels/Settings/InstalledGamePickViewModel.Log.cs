using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

public sealed partial class InstalledGamePickViewModel
{
    [LoggerMessage(EventId = 975, Level = LogLevel.Warning, Message = "Couldn't keep the game chosen for an installed game")]
    private static partial void LogNotKept(ILogger logger, Exception exception);
}
