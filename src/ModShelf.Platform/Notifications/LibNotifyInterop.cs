using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// The parts of libnotify, and of the GLib it's built on, that the Linux notices call. libnotify sends them to the
/// desktop's notification server over D-Bus, as the freedesktop.org specification describes.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class LibNotifyInterop
{
    private const string LibNotify = "libnotify.so.4";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    /// <summary>
    /// Whether libnotify is installed.
    /// </summary>
    public static bool IsAvailable() => NativeLibrary.TryLoad(LibNotify, typeof(LibNotifyInterop).Assembly, null, out _);

    /// <summary>
    /// Readies libnotify to send notices as <paramref name="appName"/>. Calling it again does nothing.
    /// </summary>
    [LibraryImport(LibNotify, EntryPoint = "notify_init", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Init(string appName);

    /// <returns>A notice, to free with <see cref="ObjectUnref"/>.</returns>
    [LibraryImport(LibNotify, EntryPoint = "notify_notification_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr NotificationNew(string summary, string? body, string? icon);

    /// <param name="error">A GError when it returns false, to free with <see cref="TakeErrorMessage"/>.</param>
    [LibraryImport(LibNotify, EntryPoint = "notify_notification_show")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool NotificationShow(IntPtr notification, out IntPtr error);

    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    public static partial void ObjectUnref(IntPtr instance);

    /// <summary>
    /// The message of <paramref name="error"/>, which is then freed; null when there's no error.
    /// </summary>
    public static string? TakeErrorMessage(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return null;
        }

        // struct GError { GQuark domain; gint code; gchar *message; }: the message follows two 32-bit fields.
        string? message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 2 * sizeof(int)));
        ErrorFree(error);
        return message;
    }

    [LibraryImport(GLib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);
}
