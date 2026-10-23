using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using ModShelf.Application.Accounts;

using static ModShelf.Platform.Secrets.LibSecretInterop;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// Keeps secrets on Linux in the desktop's keyring through the Secret Service (GNOME Keyring, KWallet and
/// others), using libsecret. The keyring encrypts them and unlocks with the user's login.
/// </summary>
/// <remarks>
/// Items use the schema <c>app.modshelf.Secret</c> with the attributes <c>scope</c> and <c>name</c>, and show up in
/// keyring tools labelled "ModShelf: name". libsecret's calls block, so they run on the thread pool, one at a time.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LibSecretSecretStore : ISecretStore
{
    private const string SchemaName = "app.modshelf.Secret";

    private static readonly Lock Gate = new();

    // Created once and kept for the life of the process, like the static schemas C programs declare.
    private static readonly Lazy<IntPtr> Schema = new(() => WithHashTable(
        [("scope", null), ("name", null)],
        names => SchemaNew(SchemaName, SchemaFlagsNone, names)));

    private readonly string _scope;

    /// <param name="scope">Keeps these secrets apart from others with the same names, such as a test run's.</param>
    public LibSecretSecretStore(string scope = "ModShelf")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        _scope = scope;
    }

    public bool IsProtectedByOs => true;

    /// <summary>
    /// Whether libsecret is installed and a Secret Service answers on the session bus.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            new LibSecretSecretStore("ModShelf.Probe").Lookup("probe");
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        return Task.Run(() => Lookup(name), cancellationToken);
    }

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);
        ArgumentNullException.ThrowIfNull(secret);

        return Task.Run(
            () =>
            {
                lock (Gate)
                {
                    WithHashTable(AttributesFor(name), attributes =>
                    {
                        // The result repeats what the error says, so only the error is checked.
                        _ = PasswordStore(Schema.Value, attributes, null, $"ModShelf: {name}", secret, IntPtr.Zero, out IntPtr error);
                        ThrowIfError(error, "store a secret");
                        return 0;
                    });
                }
            },
            cancellationToken);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        SecretNames.Validate(name);

        return Task.Run(
            () =>
            {
                lock (Gate)
                {
                    WithHashTable(AttributesFor(name), attributes =>
                    {
                        // Returns false when there was nothing to remove, which is fine.
                        _ = PasswordClear(Schema.Value, attributes, IntPtr.Zero, out IntPtr error);
                        ThrowIfError(error, "remove a secret");
                        return 0;
                    });
                }
            },
            cancellationToken);
    }

    private string? Lookup(string name)
    {
        lock (Gate)
        {
            return WithHashTable(AttributesFor(name), attributes =>
            {
                IntPtr password = PasswordLookup(Schema.Value, attributes, IntPtr.Zero, out IntPtr error);
                ThrowIfError(error, "read a secret");
                if (password == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUTF8(password);
                }
                finally
                {
                    PasswordFree(password);
                }
            });
        }
    }

    private (string Key, string? Value)[] AttributesFor(string name) => [("scope", _scope), ("name", name)];
}
