using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// A site card's log messages on the Accounts screen.
/// </summary>
public sealed partial class AccountViewModel
{
    [LoggerMessage(EventId = 916, Level = LogLevel.Error, Message = "Linking an account on {Source} failed")]
    private static partial void LogLinkFailed(ILogger logger, SourceId source, Exception exception);

    [LoggerMessage(EventId = 917, Level = LogLevel.Error, Message = "Checking the account on {Source} failed")]
    private static partial void LogCheckFailed(ILogger logger, SourceId source, Exception exception);

    [LoggerMessage(EventId = 918, Level = LogLevel.Error, Message = "Unlinking the account on {Source} failed")]
    private static partial void LogUnlinkFailed(ILogger logger, SourceId source, Exception exception);

    [LoggerMessage(EventId = 919, Level = LogLevel.Warning, Message = "The account on {Source} couldn't be listed again after a change")]
    private static partial void LogListFailed(ILogger logger, SourceId source, Exception exception);
}
