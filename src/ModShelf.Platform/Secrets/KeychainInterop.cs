using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The parts of the macOS Security and CoreFoundation frameworks the Keychain store calls.
/// </summary>
/// <remarks>
/// Every CoreFoundation object this class creates is released by the caller through <see cref="CFRelease"/>,
/// or by <see cref="Query"/> itself. The framework constants are global variables, read once through their exports.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class KeychainInterop
{
    public const int ErrSecSuccess = 0;
    public const int ErrSecItemNotFound = -25300;

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    private static readonly Lazy<Constants> Lookup = new(Constants.Load);

    public static Constants Keys => Lookup.Value;

    [LibraryImport(SecurityFramework)]
    public static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(SecurityFramework)]
    public static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(SecurityFramework)]
    public static partial int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [LibraryImport(SecurityFramework)]
    public static partial int SecItemDelete(IntPtr query);

    [LibraryImport(CoreFoundationFramework)]
    public static partial void CFRelease(IntPtr value);

    [LibraryImport(CoreFoundationFramework)]
    public static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundationFramework)]
    public static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(SecurityFramework)]
    private static partial IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);

    [LibraryImport(CoreFoundationFramework, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport(CoreFoundationFramework)]
    private static partial IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [LibraryImport(CoreFoundationFramework)]
    private static partial IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        nint count,
        IntPtr keyCallBacks,
        IntPtr valueCallBacks);

    [LibraryImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

    /// <summary>
    /// A CFDictionary of the given keys and values, which it retains. Values are CoreFoundation objects or
    /// <see cref="string"/>s and <see cref="byte"/> arrays, converted and released here.
    /// </summary>
    public static IntPtr Query(params (IntPtr Key, object Value)[] entries)
    {
        IntPtr[] keys = new IntPtr[entries.Length];
        IntPtr[] values = new IntPtr[entries.Length];
        List<IntPtr> created = [];
        try
        {
            for (int index = 0; index < entries.Length; index++)
            {
                keys[index] = entries[index].Key;
                values[index] = entries[index].Value switch
                {
                    IntPtr existing => existing,
                    string text => Track(CFStringCreateWithCString(IntPtr.Zero, text, CFStringEncodingUtf8), created),
                    byte[] bytes => Track(CFDataCreate(IntPtr.Zero, bytes, bytes.Length), created),
                    _ => throw new ArgumentException($"Unsupported value type {entries[index].Value.GetType()}.", nameof(entries)),
                };
            }

            IntPtr dictionary = CFDictionaryCreate(
                IntPtr.Zero, keys, values, entries.Length, Keys.KeyCallBacks, Keys.ValueCallBacks);
            return dictionary != IntPtr.Zero
                ? dictionary
                : throw new InvalidOperationException("CoreFoundation couldn't create a dictionary.");
        }
        finally
        {
            created.ForEach(CFRelease);
        }
    }

    /// <summary>
    /// The system's description of a Security framework status code.
    /// </summary>
    public static string Describe(int status)
    {
        IntPtr message = SecCopyErrorMessageString(status, IntPtr.Zero);
        if (message == IntPtr.Zero)
        {
            return $"error {status}";
        }

        try
        {
            byte[] buffer = new byte[1024];
            return CFStringGetCString(message, buffer, buffer.Length, CFStringEncodingUtf8)
                ? $"{Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))} ({status})"
                : $"error {status}";
        }
        finally
        {
            CFRelease(message);
        }
    }

    private static IntPtr Track(IntPtr value, List<IntPtr> created)
    {
        if (value == IntPtr.Zero)
        {
            throw new InvalidOperationException("CoreFoundation couldn't create a value.");
        }

        created.Add(value);
        return value;
    }

    /// <summary>
    /// The framework constants: CFStringRef globals for the keychain keys, and the dictionary callbacks.
    /// </summary>
    internal sealed record Constants(
        IntPtr Class,
        IntPtr ClassGenericPassword,
        IntPtr AttrService,
        IntPtr AttrAccount,
        IntPtr AttrLabel,
        IntPtr ValueData,
        IntPtr ReturnData,
        IntPtr MatchLimit,
        IntPtr MatchLimitOne,
        IntPtr True,
        IntPtr KeyCallBacks,
        IntPtr ValueCallBacks)
    {
        public static Constants Load()
        {
            IntPtr security = NativeLibrary.Load(SecurityFramework);
            IntPtr coreFoundation = NativeLibrary.Load(CoreFoundationFramework);

            // Each export is the address of a global variable holding the CFStringRef or CFBooleanRef.
            static IntPtr Read(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

            return new Constants(
                Read(security, "kSecClass"),
                Read(security, "kSecClassGenericPassword"),
                Read(security, "kSecAttrService"),
                Read(security, "kSecAttrAccount"),
                Read(security, "kSecAttrLabel"),
                Read(security, "kSecValueData"),
                Read(security, "kSecReturnData"),
                Read(security, "kSecMatchLimit"),
                Read(security, "kSecMatchLimitOne"),
                Read(coreFoundation, "kCFBooleanTrue"),

                // The callbacks are structs, passed by address.
                NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"));
        }
    }
}
