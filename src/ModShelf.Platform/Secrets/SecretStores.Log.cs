using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The secret store choice's log messages.
/// </summary>
internal static partial class SecretStores
{
    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Warning,
        Message = "No keyring (Secret Service) is running, so secrets are kept in files encrypted for this computer. Other programs running as you can still read them; install and unlock a keyring, such as GNOME Keyring or KWallet, to protect them.")]
    private static partial void LogNoKeyring(ILogger logger);

    [LoggerMessage(
        EventId = 301,
        Level = LogLevel.Error,
        Message = "The keyring (Secret Service) didn't answer as it should, so ModShelf can't read or save credentials until it does: {Reason}. Unlock or restart the keyring, such as GNOME Keyring or KWallet.")]
    private static partial void LogKeyringFailing(ILogger logger, string reason);
}
