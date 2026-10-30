using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// The Accounts screen's log messages.
/// </summary>
public sealed partial class AccountsViewModel
{
    [LoggerMessage(EventId = 920, Level = LogLevel.Error, Message = "The linked accounts couldn't be listed")]
    private static partial void LogListFailed(ILogger logger, Exception exception);
}
