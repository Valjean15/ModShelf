using ModShelf.Sources.DegMods.Catalog;

namespace ModShelf.Sources.DegMods.Downloads;

/// <summary>
/// Where DEG Mods' files are, and how ModShelf takes them from there: a Blossom file server names a file by its SHA-256,
/// so any of them may have it; a few hosts give the file itself at its link; anything else, such as MediaFire, Google
/// Drive or Mega, is a page that leads to it, which only a browser can follow.
/// </summary>
internal static class FileHosts
{
    /// <summary>
    /// The SHA-256 a file server's link names its file by, <c>&lt;server&gt;/&lt;sha256&gt;[.ext]</c>, in lowercase
    /// hexadecimal; null for another link.
    /// </summary>
    public static string? BlossomHash(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        string[] parts = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            return null;
        }

        string stem = Path.GetFileNameWithoutExtension(parts[0]);
        return stem.Length == 64 && ModEvents.IsHex(stem) ? stem.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Whether the link gives the file itself, whatever it's called: catbox.moe's files, the Internet Archive's
    /// downloads, GitHub's release downloads and raw files.
    /// </summary>
    public static bool IsDirect(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        string host = link.Host.ToLowerInvariant();
        string path = link.AbsolutePath;
        return host switch
        {
            "files.catbox.moe" => true,
            "archive.org" or "www.archive.org" => path.StartsWith("/download/", StringComparison.Ordinal),
            "github.com" => path.Contains("/releases/download/", StringComparison.Ordinal),
            "raw.githubusercontent.com" => true,
            _ => false,
        };
    }
}
