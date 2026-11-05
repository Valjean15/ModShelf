using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The log message of Settings' Add a game….
/// </summary>
public sealed partial class AddGameViewModel
{
    [LoggerMessage(EventId = 952, Level = LogLevel.Error, Message = "Reading the games of {Source} to add one failed")]
    private static partial void LogGamesUnreadable(ILogger logger, Exception exception, string source);
}
