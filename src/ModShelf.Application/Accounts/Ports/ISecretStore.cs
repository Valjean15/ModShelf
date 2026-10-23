namespace ModShelf.Application.Accounts.Ports;

/// <summary>
/// Keeps secrets, such as account credentials, in the operating system's protected storage. Implemented by
/// Platform: DPAPI on Windows, the Keychain on macOS, the Secret Service on Linux.
/// </summary>
/// <remarks>
/// Secrets never go to the library database, settings files or the log. Names are short ASCII keys such as
/// <c>account/nexus</c>; the store keeps them apart from other apps' secrets.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Whether the operating system protects the secrets. False for the Linux fallback without a keyring,
    /// which Settings must point out to the user.
    /// </summary>
    bool IsProtectedByOs { get; }

    /// <returns>The secret, or null when there's none under <paramref name="name"/>.</returns>
    Task<string?> ReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="secret"/> under <paramref name="name"/>, replacing any secret already there.
    /// </summary>
    Task WriteAsync(string name, string secret, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the secret under <paramref name="name"/>. Does nothing when there's none.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}
