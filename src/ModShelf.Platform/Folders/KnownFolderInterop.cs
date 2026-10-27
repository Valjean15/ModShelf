using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModShelf.Platform.Folders;

/// <summary>
/// The Windows Known Folders call, which says where the user's folders are, wherever they moved them.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class KnownFolderInterop
{
    /// <summary>
    /// FOLDERID_Downloads.
    /// </summary>
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// The folder's path, or null when Windows can't say.
    /// </summary>
    public static string? PathOf(Guid folder)
    {
        int result = SHGetKnownFolderPath(in folder, 0, IntPtr.Zero, out IntPtr path);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
