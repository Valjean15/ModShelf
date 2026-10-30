using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The parts of libsecret and GLib the Linux store calls. Uses only the non-variadic "v" functions, which .NET
/// can call directly; every argument is "transfer none", so the caller frees what it allocates.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class LibSecretInterop
{
    /// <summary>SECRET_SCHEMA_NONE: items must match the schema name too.</summary>
    public const int SchemaFlagsNone = 0;

    /// <summary>SECRET_SEARCH_NONE: the first match, locked or not, without unlocking it or loading its secret.</summary>
    public const int SearchFlagsNone = 0;

    private const string LibSecret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    /// <summary>SECRET_SCHEMA_ATTRIBUTE_STRING, as the pointer-sized value GLib hash tables hold.</summary>
    public static readonly IntPtr AttributeTypeString = IntPtr.Zero;

    private static readonly Lazy<(IntPtr Hash, IntPtr Equal)> StringFunctions = new(() =>
    {
        IntPtr glib = NativeLibrary.Load(GLib);
        return (NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
    });

    [LibraryImport(LibSecret, EntryPoint = "secret_schema_newv", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr SchemaNew(string name, int flags, IntPtr attributeNamesAndTypes);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int PasswordStore(
        IntPtr schema,
        IntPtr attributes,
        string? collection,
        string label,
        string password,
        IntPtr cancellable,
        out IntPtr error);

    /// <returns>The password, to free with <see cref="PasswordFree"/>; zero when there's none or on error.</returns>
    [LibraryImport(LibSecret, EntryPoint = "secret_password_lookupv_sync")]
    public static partial IntPtr PasswordLookup(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    /// <summary>Overwrites the password's memory, then frees it.</summary>
    [LibraryImport(LibSecret, EntryPoint = "secret_password_free")]
    public static partial void PasswordFree(IntPtr password);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_clearv_sync")]
    public static partial int PasswordClear(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    /// <returns>
    /// A GList of the matching items, locked or not, to free with <see cref="FreeObjectList"/>; zero when none match or
    /// on error. Needs libsecret 0.19 or later.
    /// </returns>
    [LibraryImport(LibSecret, EntryPoint = "secret_password_searchv_sync")]
    public static partial IntPtr PasswordSearch(IntPtr schema, IntPtr attributes, int flags, IntPtr cancellable, out IntPtr error);

    /// <summary>
    /// Frees a GList and the GObjects it holds.
    /// </summary>
    public static void FreeObjectList(IntPtr list) => ListFreeFull(list, ObjectUnref.Value);

    /// <summary>
    /// Calls <paramref name="use"/> with a GLib string hash table holding <paramref name="entries"/>, then frees the
    /// table and its strings. A null value is stored as a null pointer.
    /// </summary>
    public static T WithHashTable<T>(IReadOnlyList<(string Key, string? Value)> entries, Func<IntPtr, T> use)
    {
        (IntPtr hash, IntPtr equal) = StringFunctions.Value;
        List<IntPtr> strings = [];
        IntPtr table = HashTableNew(hash, equal);
        try
        {
            foreach ((string key, string? value) in entries)
            {
                IntPtr nativeKey = Marshal.StringToCoTaskMemUTF8(key);
                strings.Add(nativeKey);
                IntPtr nativeValue = value is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(value);
                if (nativeValue != IntPtr.Zero)
                {
                    strings.Add(nativeValue);
                }

                // Reports only whether the key was new, which it always is in a fresh table.
                _ = HashTableInsert(table, nativeKey, nativeValue);
            }

            return use(table);
        }
        finally
        {
            HashTableUnref(table);
            strings.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    /// <summary>
    /// Frees <paramref name="error"/> and throws with its message, when there's an error.
    /// </summary>
    public static void ThrowIfError(IntPtr error, string action)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        (_, _, string? message) = ReadError(error);
        throw new InvalidOperationException($"The Secret Service couldn't {action}: {message}");
    }

    /// <summary>
    /// Reads <paramref name="error"/>, then frees it.
    /// </summary>
    /// <returns>GLib's name for its domain, such as <c>g-dbus-error-quark</c>; its code in that domain; and its message.</returns>
    public static (string? Domain, int Code, string? Message) ReadError(IntPtr error)
    {
        // struct GError { GQuark domain; gint code; gchar *message; }: the message follows two 32-bit fields.
        string? domain = Marshal.PtrToStringUTF8(QuarkToString(unchecked((uint)Marshal.ReadInt32(error))));
        int code = Marshal.ReadInt32(error, sizeof(int));
        string? message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 2 * sizeof(int)));
        ErrorFree(error);
        return (domain, code, message);
    }

    private static readonly Lazy<IntPtr> ObjectUnref = new(() => NativeLibrary.GetExport(NativeLibrary.Load(GObject), "g_object_unref"));

    [LibraryImport(GLib, EntryPoint = "g_list_free_full")]
    private static partial void ListFreeFull(IntPtr list, IntPtr freeFunction);

    [LibraryImport(GLib, EntryPoint = "g_hash_table_new")]
    private static partial IntPtr HashTableNew(IntPtr hashFunction, IntPtr equalFunction);

    [LibraryImport(GLib, EntryPoint = "g_hash_table_insert")]
    private static partial int HashTableInsert(IntPtr table, IntPtr key, IntPtr value);

    [LibraryImport(GLib, EntryPoint = "g_hash_table_unref")]
    private static partial void HashTableUnref(IntPtr table);

    [LibraryImport(GLib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);

    /// <returns>The quark's string, which GLib owns.</returns>
    [LibraryImport(GLib, EntryPoint = "g_quark_to_string")]
    private static partial IntPtr QuarkToString(uint quark);
}
