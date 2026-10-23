using System.Security.Cryptography;
using System.Text;

using ModShelf.Application.Accounts;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The fallback for Linux without a keyring: one file per secret, encrypted with AES-GCM under a key derived
/// from this computer's machine ID, the user's name and the secret's name. Files are readable only by the user.
/// </summary>
/// <remarks>
/// This keeps secrets unreadable in backups, synced folders or a copied home folder, because the key never
/// leaves this computer. It doesn't protect them from programs running as the same user, which can derive
/// the same key; only a keyring does. <see cref="IsProtectedByOs"/> is therefore false, and Settings says so.
/// </remarks>
public sealed class EncryptedFileSecretStore : ISecretStore
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("ModShelf encrypted secret file v1");
    private static readonly string[] MachineIdFiles = ["/etc/machine-id", "/var/lib/dbus/machine-id"];

    private readonly string _folder;
    private readonly Lazy<byte[]> _machineId;

    /// <param name="folder">The folder for the encrypted files, such as <c>~/.local/share/ModShelf/Secrets</c>.</param>
    /// <param name="machineId">
    /// What ties the secrets to this computer. By default the system's machine ID, or, where there's none,
    /// a random ID kept in <paramref name="folder"/>.
    /// </param>
    public EncryptedFileSecretStore(string folder, string? machineId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!AesGcm.IsSupported)
        {
            throw new PlatformNotSupportedException("This system can't do AES-GCM encryption.");
        }

        _folder = folder;
        _machineId = new Lazy<byte[]>(() => Encoding.UTF8.GetBytes(machineId ?? ReadMachineId()));
    }

    public bool IsProtectedByOs => false;

    /// <exception cref="InvalidDataException">
    /// The file can't be decrypted: it's damaged, or was made on another computer or by another user.
    /// </exception>
    public async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        byte[]? file = await SecretFiles.ReadAsync(PathOf(name), cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        if (file.Length < HeaderSize || file[0] != FormatVersion)
        {
            throw new InvalidDataException($"The secret '{name}' isn't in a format this version of ModShelf reads.");
        }

        byte[] plaintext = new byte[file.Length - HeaderSize];
        try
        {
            using AesGcm aes = new(KeyFor(name), TagSize);
            aes.Decrypt(
                file.AsSpan(1, NonceSize),
                file.AsSpan(HeaderSize),
                file.AsSpan(1 + NonceSize, TagSize),
                plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                $"The secret '{name}' can't be decrypted. It may be damaged, or come from another computer or user.", exception);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    public async Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] file = new byte[HeaderSize + plaintext.Length];
        file[0] = FormatVersion;
        RandomNumberGenerator.Fill(file.AsSpan(1, NonceSize));
        using (AesGcm aes = new(KeyFor(name), TagSize))
        {
            aes.Encrypt(
                file.AsSpan(1, NonceSize),
                plaintext,
                file.AsSpan(HeaderSize),
                file.AsSpan(1 + NonceSize, TagSize));
        }

        SecretFiles.EnsureFolder(_folder);
        await SecretFiles.WriteAsync(PathOf(name), file, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        cancellationToken.ThrowIfCancellationRequested();

        SecretFiles.Delete(PathOf(name));
        return Task.CompletedTask;
    }

    // Each secret gets its own key, so a file copied over another secret's file doesn't decrypt.
    private byte[] KeyFor(string name)
        => HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _machineId.Value,
            outputLength: 32,
            Salt,
            Encoding.UTF8.GetBytes($"{Environment.UserName}\n{name}"));

    private string ReadMachineId()
    {
        foreach (string file in MachineIdFiles)
        {
            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } id)
            {
                return id;
            }
        }

        // No machine ID, as in some containers: keep a random one beside the secrets instead.
        string local = Path.Combine(_folder, ".machine-id");
        SecretFiles.EnsureFolder(_folder);
        if (!File.Exists(local))
        {
            SecretFiles.WriteAsync(local, Encoding.UTF8.GetBytes(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))), CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        return File.ReadAllText(local).Trim();
    }

    private string PathOf(string name) => Path.Combine(_folder, SecretNames.ToFileName(name));
}
