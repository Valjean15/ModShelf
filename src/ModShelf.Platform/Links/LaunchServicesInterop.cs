using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ModShelf.Platform.Links;

/// <summary>
/// The parts of macOS' Launch Services and CoreFoundation that the link scheme registration calls: which app opens a
/// scheme's links, this app's bundle ID, and its preferences.
/// </summary>
/// <remarks>
/// <c>LSCopyDefaultHandlerForURLScheme</c> and <c>LSSetDefaultHandlerForURLScheme</c> are deprecated since macOS 12 in
/// favour of <c>NSWorkspace</c>'s asynchronous methods, which need Objective-C; they still work, and need no prompt for a
/// scheme of a mod site's own.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class LaunchServicesInterop
{
    private const string CoreServicesFramework = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    /// <summary>
    /// The bundle ID of the app that opens <paramref name="scheme"/>'s links, such as <c>com.apple.safari</c>; null for
    /// none.
    /// </summary>
    public static string? DefaultHandler(string scheme)
    {
        IntPtr name = Create(scheme);
        try
        {
            return Take(LSCopyDefaultHandlerForURLScheme(name));
        }
        finally
        {
            CFRelease(name);
        }
    }

    /// <summary>
    /// Makes the app with <paramref name="bundleId"/> open <paramref name="scheme"/>'s links.
    /// </summary>
    /// <exception cref="IOException">Launch Services refused.</exception>
    public static void SetDefaultHandler(string scheme, string bundleId)
    {
        IntPtr name = Create(scheme);
        IntPtr id = Create(bundleId);
        try
        {
            int status = LSSetDefaultHandlerForURLScheme(name, id);
            if (status != 0)
            {
                throw new IOException($"Launch Services refused to set the app that opens {scheme}:// links (error {status}).");
            }
        }
        finally
        {
            CFRelease(id);
            CFRelease(name);
        }
    }

    /// <summary>
    /// This app's bundle ID; null when it isn't running from an app bundle, such as with <c>dotnet run</c>.
    /// </summary>
    public static string? MainBundleId()
    {
        IntPtr bundle = CFBundleGetMainBundle();
        return bundle == IntPtr.Zero ? null : Read(CFBundleGetIdentifier(bundle));
    }

    /// <summary>
    /// A string from the preferences of the app with <paramref name="bundleId"/>; null when it's not set.
    /// </summary>
    public static string? GetPreference(string bundleId, string key)
    {
        IntPtr app = Create(bundleId);
        IntPtr name = Create(key);
        try
        {
            return Take(CFPreferencesCopyAppValue(name, app));
        }
        finally
        {
            CFRelease(name);
            CFRelease(app);
        }
    }

    /// <summary>
    /// Sets a string in the preferences of the app with <paramref name="bundleId"/>, or removes it for null, and saves
    /// them.
    /// </summary>
    /// <exception cref="IOException">The preferences couldn't be saved.</exception>
    public static void SetPreference(string bundleId, string key, string? value)
    {
        IntPtr app = Create(bundleId);
        IntPtr name = Create(key);
        IntPtr text = value is null ? IntPtr.Zero : Create(value);
        try
        {
            CFPreferencesSetAppValue(name, text, app);
            if (!CFPreferencesAppSynchronize(app))
            {
                throw new IOException("macOS couldn't save ModShelf's preferences.");
            }
        }
        finally
        {
            if (text != IntPtr.Zero)
            {
                CFRelease(text);
            }

            CFRelease(name);
            CFRelease(app);
        }
    }

    private static IntPtr Create(string text)
    {
        IntPtr created = CFStringCreateWithCString(IntPtr.Zero, text, CFStringEncodingUtf8);
        return created != IntPtr.Zero ? created : throw new InvalidOperationException("CoreFoundation couldn't create a string.");
    }

    /// <summary>
    /// The text of a string this code owns, which it releases; null for none, or a value that isn't a string.
    /// </summary>
    private static string? Take(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return CFGetTypeID(value) == CFStringGetTypeID() ? Read(value) : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>
    /// The text of a string this code doesn't own.
    /// </summary>
    private static string? Read(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        nint size = CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), CFStringEncodingUtf8) + 1;
        byte[] buffer = new byte[size];
        return CFStringGetCString(value, buffer, size, CFStringEncodingUtf8)
            ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
            : null;
    }

    [LibraryImport(CoreServicesFramework)]
    private static partial IntPtr LSCopyDefaultHandlerForURLScheme(IntPtr scheme);

    [LibraryImport(CoreServicesFramework)]
    private static partial int LSSetDefaultHandlerForURLScheme(IntPtr scheme, IntPtr bundleId);

    [LibraryImport(CoreFoundationFramework)]
    private static partial IntPtr CFBundleGetMainBundle();

    [LibraryImport(CoreFoundationFramework)]
    private static partial IntPtr CFBundleGetIdentifier(IntPtr bundle);

    [LibraryImport(CoreFoundationFramework)]
    private static partial IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

    [LibraryImport(CoreFoundationFramework)]
    private static partial void CFPreferencesSetAppValue(IntPtr key, IntPtr value, IntPtr applicationId);

    [LibraryImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFPreferencesAppSynchronize(IntPtr applicationId);

    [LibraryImport(CoreFoundationFramework, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFStringGetLength(IntPtr value);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [LibraryImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundationFramework)]
    private static partial void CFRelease(IntPtr value);
}
