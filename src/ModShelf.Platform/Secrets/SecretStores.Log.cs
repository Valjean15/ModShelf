using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The secret store choice's log messages.
/// </summary>
public static partial class SecretStores
{
    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Warning,
        Message = "No keyring (Secret Service) is running, so secrets are kept in files encrypted for this computer. Other programs running as you can still read them; install and unlock a keyring, such as GNOME Keyring or KWallet, to protect them.")]
    private static partial void LogNoKeyring(ILogger logger);
}
