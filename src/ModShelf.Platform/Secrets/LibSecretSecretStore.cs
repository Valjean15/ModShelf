using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using ModShelf.Application.Accounts.Ports;

using static ModShelf.Platform.Secrets.LibSecretInterop;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// Keeps secrets on Linux in the desktop's keyring through the Secret Service (GNOME Keyring, KWallet and
/// others), using libsecret. The keyring encrypts them and unlocks with the user's login.
/// </summary>
/// <remarks>
/// Items use the schema <c>app.modshelf.Secret</c> with the attributes <c>scope</c> and <c>name</c>, and show up in
/// keyring tools labelled "ModShelf: name". libsecret's calls block, so they run on the thread pool, one at a time.
/// <para>
/// A keyring that's locked, whose unlock prompt was dismissed or can't be shown, answers a lookup with nothing, as if no
/// secret were stored. Reading tells the two apart, and says the keyring is locked, so ModShelf doesn't take the
/// account for unlinked.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LibSecretSecretStore : ISecretStore
{
    private const string SchemaName = "app.modshelf.Secret";

    private const string DBusErrors = "g-dbus-error-quark";
    private const string IoErrors = "g-io-error-quark";

    // GDBusError codes: no program owns the Secret Service's name, or can be started to; or there's no bus.
    private const int DBusServiceUnknown = 2;
    private const int DBusNameHasNoOwner = 3;
    private const int DBusNoServer = 11;

    // GIOErrorEnum codes from a call that was cancelled, or that a service on the bus didn't answer in time.
    private const int IoCancelled = 19;
    private const int IoTimedOut = 24;

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
    public static bool IsAvailable() => Probe() is null;

    /// <summary>
    /// Asks the Secret Service for a secret that isn't there, to see whether it answers.
    /// </summary>
    /// <returns>
    /// Null when it answered. Otherwise why not, and whether that's because there's no keyring: libsecret isn't
    /// installed, there's no session bus, or no Secret Service on it; rather than a keyring that's there but failing.
    /// </returns>
    public static (bool NoKeyring, string Reason)? Probe()
    {
        try
        {
            lock (Gate)
            {
                return WithHashTable(
                    [("scope", "ModShelf.Probe"), ("name", "probe")],
                    attributes =>
                    {
                        IntPtr password = PasswordLookup(Schema.Value, attributes, IntPtr.Zero, out IntPtr error);
                        if (password != IntPtr.Zero)
                        {
                            PasswordFree(password);
                        }

                        if (error == IntPtr.Zero)
                        {
                            return ((bool, string)?)null;
                        }

                        (string? domain, int code, string? message) = ReadError(error);
                        return (MeansNoKeyring(domain, code), message ?? $"{domain} error {code}");
                    });
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return (true, exception.Message);
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

    /// <summary>
    /// Whether an error asking the Secret Service means there's none: no program owns its name on the bus, or can be
    /// started to, or there's no bus to connect to. Other errors, such as a call it didn't answer in time, come from a
    /// keyring that's there but failing.
    /// </summary>
    /// <param name="domain">GLib's name for the error's domain.</param>
    /// <param name="code">The error's code in that domain.</param>
    internal static bool MeansNoKeyring(string? domain, int code) => domain switch
    {
        DBusErrors => code is DBusServiceUnknown or DBusNameHasNoOwner or DBusNoServer,

        // Connecting to the bus fails with these, such as "Cannot autolaunch D-Bus without X11 $DISPLAY".
        IoErrors => code is not (IoCancelled or IoTimedOut),
        _ => false,
    };

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
                    return IsStored(attributes)
                        ? throw new InvalidOperationException(
                            "The keyring that holds ModShelf's secrets is locked. Unlock it, such as by signing in to your desktop again, then try again.")
                        : null;
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

    /// <summary>
    /// Whether an item with these attributes is stored, locked or not. Without libsecret 0.19, which can tell, it's
    /// taken as not.
    /// </summary>
    private static bool IsStored(IntPtr attributes)
    {
        IntPtr items;
        try
        {
            items = PasswordSearch(Schema.Value, attributes, SearchFlagsNone, IntPtr.Zero, out IntPtr error);
            ThrowIfError(error, "read a secret");
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }

        if (items == IntPtr.Zero)
        {
            return false;
        }

        FreeObjectList(items);
        return true;
    }

    private (string Key, string? Value)[] AttributesFor(string name) => [("scope", _scope), ("name", name)];
}
