using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Application.Accounts;

/// <summary>
/// The credential vault's log messages. They name the source only: never the stored credentials.
/// </summary>
internal sealed partial class CredentialVault
{
    [LoggerMessage(EventId = 405, Level = LogLevel.Warning, Message = "The stored credentials for {Source} can't be read; the account has to be linked again")]
    private static partial void LogUnreadable(ILogger logger, SourceId source);

    [LoggerMessage(EventId = 410, Level = LogLevel.Warning, Message = "Couldn't read the stored credentials for {Source} again; using the ones read before")]
    private static partial void LogCantReadAgain(ILogger logger, Exception exception, SourceId source);
}
