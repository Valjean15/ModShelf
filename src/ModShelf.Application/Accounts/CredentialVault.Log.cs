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
}
