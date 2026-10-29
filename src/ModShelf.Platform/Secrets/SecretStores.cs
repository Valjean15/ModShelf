using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// Picks the secret store for the system the app runs on.
/// </summary>
internal static partial class SecretStores
{
    /// <summary>
    /// ModShelf's folder for secret files under the user's local application data, such as
    /// <c>%LOCALAPPDATA%\ModShelf\Secrets</c>. Used on Windows, and by the Linux fallback.
    /// </summary>
    /// <exception cref="InvalidOperationException">The system has no local application data folder for this user.</exception>
    public static string GetDefaultFolder()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(appData)
            ? throw new InvalidOperationException("This user has no local application data folder for ModShelf's secrets.")
            : Path.Combine(appData, "ModShelf", "Secrets");
    }

    /// <summary>
    /// DPAPI on Windows, the Keychain on macOS, and on Linux the Secret Service unless there's none (see
    /// <see cref="ForLinux"/>); otherwise the encrypted-file fallback, which logs a warning and reports
    /// <see cref="ISecretStore.IsProtectedByOs"/> as false.
    /// </summary>
    /// <param name="folder">The folder for secret files, used by the Windows store and the fallback.</param>
    /// <param name="scope">What keeps these secrets apart in the Keychain and the Secret Service.</param>
    public static ISecretStore ForThisSystem(string folder, string scope, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(logger);

        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretStore(folder);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainSecretStore(scope);
        }

        if (OperatingSystem.IsLinux())
        {
            return ForLinux(folder, scope, LibSecretSecretStore.Probe(), logger);
        }

        LogNoKeyring(logger);
        return new EncryptedFileSecretStore(folder);
    }

    /// <summary>
    /// The Secret Service, unless there's none: then the encrypted-file fallback, which logs a warning. A keyring that's
    /// there but failed the probe is used all the same, and that's logged as an error: reading and saving secrets fail
    /// until it works, rather than secrets going quietly to files the user didn't choose.
    /// </summary>
    /// <param name="probe">What <see cref="LibSecretSecretStore.Probe"/> found.</param>
    [SupportedOSPlatform("linux")]
    internal static ISecretStore ForLinux(string folder, string scope, (bool NoKeyring, string Reason)? probe, ILogger logger)
    {
        switch (probe)
        {
            case null:
                return new LibSecretSecretStore(scope);
            case { NoKeyring: false, Reason: var reason }:
                LogKeyringFailing(logger, reason);
                return new LibSecretSecretStore(scope);
            default:
                LogNoKeyring(logger);
                return new EncryptedFileSecretStore(folder);
        }
    }
}
