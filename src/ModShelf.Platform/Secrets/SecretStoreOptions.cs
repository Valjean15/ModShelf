namespace ModShelf.Platform.Secrets;

/// <summary>
/// Where the secret store keeps ModShelf's secrets. The defaults are the user's own; tests and other separate runs
/// set their own, so they never touch the user's accounts.
/// </summary>
public sealed record SecretStoreOptions
{
    /// <summary>
    /// The scope ModShelf's secrets use unless told otherwise.
    /// </summary>
    public const string DefaultScope = "ModShelf";

    /// <summary>
    /// The folder for secret files: the DPAPI-protected files on Windows, and the encrypted files of the Linux
    /// fallback. By default ModShelf's folder under the user's local application data.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// Keeps these secrets apart from others in the system keyring: the Keychain service name on macOS, and an
    /// attribute of every item in the Secret Service on Linux. Files are kept apart by <see cref="Folder"/> instead.
    /// </summary>
    public string Scope { get; init; } = DefaultScope;
}
