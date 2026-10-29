using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModShelf.Platform.Tray;

/// <summary>
/// Asks the session bus, through GIO, whether a program owns a name: here, whether a tray host
/// (<c>org.kde.StatusNotifierWatcher</c>) runs. Uses only non-variadic functions, which .NET can call directly.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class StatusNotifierInterop
{
    /// <summary>
    /// The name a tray host owns on the session bus while it runs, as Avalonia's tray icons look for it.
    /// </summary>
    public const string WatcherName = "org.kde.StatusNotifierWatcher";

    private const string Gio = "libgio-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const int SessionBus = 2;
    private const int NoCallFlags = 0;

    /// <summary>
    /// Whether a program owns <paramref name="name"/> on the session bus.
    /// </summary>
    /// <exception cref="InvalidOperationException">There's no session bus, or it didn't answer. The message says why.</exception>
    /// <exception cref="DllNotFoundException">GIO isn't installed.</exception>
    public static bool NameHasOwner(string name, TimeSpan timeout)
    {
        IntPtr connection = BusGet(SessionBus, IntPtr.Zero, out IntPtr error);
        ThrowIfError(error);
        try
        {
            IntPtr argument = VariantNewString(name);
            IntPtr parameters = VariantNewTuple([argument], 1);
            IntPtr reply = ConnectionCall(
                connection,
                "org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus",
                "NameHasOwner",
                parameters,
                IntPtr.Zero,
                NoCallFlags,
                (int)timeout.TotalMilliseconds,
                IntPtr.Zero,
                out error);
            ThrowIfError(error);
            try
            {
                IntPtr owned = VariantGetChildValue(reply, 0);
                try
                {
                    return VariantGetBoolean(owned) != 0;
                }
                finally
                {
                    VariantUnref(owned);
                }
            }
            finally
            {
                VariantUnref(reply);
            }
        }
        finally
        {
            ObjectUnref(connection);
        }
    }

    /// <summary>
    /// Frees <paramref name="error"/> and throws with its message, when there's an error.
    /// </summary>
    private static void ThrowIfError(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        // struct GError { GQuark domain; gint code; gchar *message; }: the message follows two 32-bit fields.
        string? message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 2 * sizeof(int)));
        ErrorFree(error);
        throw new InvalidOperationException(message);
    }

    [LibraryImport(Gio, EntryPoint = "g_bus_get_sync")]
    private static partial IntPtr BusGet(int busType, IntPtr cancellable, out IntPtr error);

    [LibraryImport(Gio, EntryPoint = "g_dbus_connection_call_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr ConnectionCall(
        IntPtr connection,
        string busName,
        string objectPath,
        string interfaceName,
        string methodName,
        IntPtr parameters,
        IntPtr replyType,
        int flags,
        int timeoutMilliseconds,
        IntPtr cancellable,
        out IntPtr error);

    /// <returns>A floating reference, which the tuple takes.</returns>
    [LibraryImport(GLib, EntryPoint = "g_variant_new_string", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr VariantNewString(string value);

    /// <returns>A floating reference, which the call takes.</returns>
    [LibraryImport(GLib, EntryPoint = "g_variant_new_tuple")]
    private static partial IntPtr VariantNewTuple(IntPtr[] children, nuint count);

    [LibraryImport(GLib, EntryPoint = "g_variant_get_child_value")]
    private static partial IntPtr VariantGetChildValue(IntPtr value, nuint index);

    [LibraryImport(GLib, EntryPoint = "g_variant_get_boolean")]
    private static partial int VariantGetBoolean(IntPtr value);

    [LibraryImport(GLib, EntryPoint = "g_variant_unref")]
    private static partial void VariantUnref(IntPtr value);

    [LibraryImport(GLib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);

    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(IntPtr instance);
}
