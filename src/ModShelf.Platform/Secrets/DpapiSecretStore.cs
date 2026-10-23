using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// Keeps secrets on Windows, encrypted with DPAPI for the current user: one file per secret, which only this
/// Windows account on this computer can decrypt.
/// </summary>
/// <remarks>
/// Each secret's name is mixed into its encryption, so a file copied over another secret's file doesn't decrypt.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _folder;

    /// <param name="folder">The folder for the encrypted files, such as <c>%LOCALAPPDATA%\ModShelf\Secrets</c>.</param>
    public DpapiSecretStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        _folder = folder;
    }

    public bool IsProtectedByOs => true;

    /// <exception cref="InvalidDataException">The file can't be decrypted: it's damaged, or belongs to another user.</exception>
    public async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        byte[]? encrypted = await SecretFiles.ReadAsync(PathOf(name), cancellationToken).ConfigureAwait(false);
        if (encrypted is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, EntropyFor(name), DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                $"The secret '{name}' can't be decrypted. It may be damaged, or belong to another Windows user.", exception);
        }
    }

    public async Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), EntropyFor(name), DataProtectionScope.CurrentUser);
        SecretFiles.EnsureFolder(_folder);
        await SecretFiles.WriteAsync(PathOf(name), encrypted, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        cancellationToken.ThrowIfCancellationRequested();

        SecretFiles.Delete(PathOf(name));
        return Task.CompletedTask;
    }

    private static byte[] EntropyFor(string name) => Encoding.UTF8.GetBytes($"ModShelf secret: {name}");

    private string PathOf(string name) => Path.Combine(_folder, SecretNames.ToFileName(name));
}
