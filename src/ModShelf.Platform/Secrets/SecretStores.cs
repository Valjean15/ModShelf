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
    /// DPAPI on Windows, the Keychain on macOS, and on Linux the Secret Service when one is running; otherwise
    /// the encrypted-file fallback, which logs a warning and reports <see cref="ISecretStore.IsProtectedByOs"/> as false.
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

        if (OperatingSystem.IsLinux() && LibSecretSecretStore.IsAvailable())
        {
            return new LibSecretSecretStore(scope);
        }

        LogNoKeyring(logger);
        return new EncryptedFileSecretStore(folder);
    }
}
