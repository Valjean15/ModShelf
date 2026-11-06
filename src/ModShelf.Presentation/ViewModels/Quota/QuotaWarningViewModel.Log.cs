using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Quota;

/// <summary>
/// The quota strip's log messages.
/// </summary>
public sealed partial class QuotaWarningViewModel
{
    [LoggerMessage(EventId = 958, Level = LogLevel.Warning, Message = "The quota strip couldn't read the settings, so it warns below the default share")]
    private static partial void LogSettingsUnread(ILogger logger, Exception exception);
}
