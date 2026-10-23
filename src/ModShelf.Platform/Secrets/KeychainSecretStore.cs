using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using ModShelf.Application.Accounts.Ports;

using static ModShelf.Platform.Secrets.KeychainInterop;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// Keeps secrets on macOS in the user's login Keychain, as generic passwords: service <c>ModShelf</c>, account
/// the secret's name, label "ModShelf: name".
/// </summary>
/// <remarks>
/// Keychain calls can block while macOS asks the user to unlock or allow access, so they run on the thread pool,
/// one at a time. An unsigned build that changes, such as after an update, may get one such prompt.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class KeychainSecretStore : ISecretStore
{
    private static readonly Lock Gate = new();

    private readonly string _service;

    /// <param name="service">Keeps these secrets apart from others with the same names, such as a test run's.</param>
    public KeychainSecretStore(string service = "ModShelf")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        _service = service;
    }

    public bool IsProtectedByOs => true;

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        return Task.Run(() => Read(name), cancellationToken);
    }

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        ArgumentNullException.ThrowIfNull(secret);

        return Task.Run(() => Write(name, Encoding.UTF8.GetBytes(secret)), cancellationToken);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        return Task.Run(() => Delete(name), cancellationToken);
    }

    private static void ThrowIfFailed(int status, string action)
    {
        if (status != ErrSecSuccess)
        {
            throw new InvalidOperationException($"The Keychain couldn't {action}: {Describe(status)}");
        }
    }

    private string? Read(string name)
    {
        lock (Gate)
        {
            IntPtr query = Query(
                (Keys.Class, Keys.ClassGenericPassword),
                (Keys.AttrService, _service),
                (Keys.AttrAccount, name),
                (Keys.ReturnData, Keys.True),
                (Keys.MatchLimit, Keys.MatchLimitOne));
            try
            {
                int status = SecItemCopyMatching(query, out IntPtr data);
                if (status == ErrSecItemNotFound)
                {
                    return null;
                }

                ThrowIfFailed(status, "read a secret");
                try
                {
                    byte[] bytes = new byte[CFDataGetLength(data)];
                    Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    CFRelease(data);
                }
            }
            finally
            {
                CFRelease(query);
            }
        }
    }

    private void Write(string name, byte[] secret)
    {
        lock (Gate)
        {
            IntPtr query = Query((Keys.Class, Keys.ClassGenericPassword), (Keys.AttrService, _service), (Keys.AttrAccount, name));
            IntPtr update = Query((Keys.ValueData, secret));
            try
            {
                int status = SecItemUpdate(query, update);
                if (status == ErrSecItemNotFound)
                {
                    IntPtr item = Query(
                        (Keys.Class, Keys.ClassGenericPassword),
                        (Keys.AttrService, _service),
                        (Keys.AttrAccount, name),
                        (Keys.AttrLabel, $"ModShelf: {name}"),
                        (Keys.ValueData, secret));
                    try
                    {
                        status = SecItemAdd(item, IntPtr.Zero);
                    }
                    finally
                    {
                        CFRelease(item);
                    }
                }

                ThrowIfFailed(status, "store a secret");
            }
            finally
            {
                CFRelease(update);
                CFRelease(query);
            }
        }
    }

    private void Delete(string name)
    {
        lock (Gate)
        {
            IntPtr query = Query((Keys.Class, Keys.ClassGenericPassword), (Keys.AttrService, _service), (Keys.AttrAccount, name));
            try
            {
                int status = SecItemDelete(query);
                if (status != ErrSecItemNotFound)
                {
                    ThrowIfFailed(status, "remove a secret");
                }
            }
            finally
            {
                CFRelease(query);
            }
        }
    }
}
