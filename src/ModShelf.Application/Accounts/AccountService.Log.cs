using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// The account service's log messages. They name sources and kinds only: never credentials, answers or the
/// account's profile.
/// </summary>
internal sealed partial class AccountService
{
    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Linked an account on {Source} with {Kind}")]
    private static partial void LogLinked(ILogger logger, SourceId source, AuthKind kind);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "Unlinked the account on {Source}")]
    private static partial void LogUnlinked(ILogger logger, SourceId source);

    [LoggerMessage(EventId = 402, Level = LogLevel.Debug, Message = "Refreshed the credentials for {Source}")]
    private static partial void LogRefreshed(ILogger logger, SourceId source);

    [LoggerMessage(EventId = 403, Level = LogLevel.Warning, Message = "Couldn't refresh the credentials for {Source}; using them as they are for now")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, SourceId source);

    [LoggerMessage(EventId = 404, Level = LogLevel.Warning, Message = "The credentials for {Source} can no longer be refreshed; the account has to be linked again")]
    private static partial void LogNeedsRelink(ILogger logger, SourceId source);

    [LoggerMessage(EventId = 406, Level = LogLevel.Warning, Message = "Can't refresh the credentials for {Source}: the source isn't registered or no longer offers {Kind}")]
    private static partial void LogNoStrategy(ILogger logger, SourceId source, AuthKind kind);
}
