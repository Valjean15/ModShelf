using Microsoft.Extensions.Logging;

using ModShelf.Application.Settings;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// The pictures' log messages.
/// </summary>
public sealed partial class ModPictures
{
    [LoggerMessage(EventId = 1570, Level = LogLevel.Information, Message = "The settings can't be read, so mods' pictures show as they do by default: {Reason}")]
    private static partial void LogSettingsUnreadable(ILogger logger, string reason);

    [LoggerMessage(EventId = 1571, Level = LogLevel.Warning, Message = "The {Screen} screen's previews couldn't be saved, so they're kept until ModShelf closes: {Reason}")]
    private static partial void LogPreviewsNotSaved(ILogger logger, PreviewScreen screen, string reason);

    [LoggerMessage(EventId = 1575, Level = LogLevel.Warning, Message = "The previews of {Source}/{Game} couldn't be saved, so they're kept until ModShelf closes: {Reason}")]
    private static partial void LogGamePreviewsNotSaved(ILogger logger, string source, string game, string reason);
}
