using System.Net;
using System.Net.Sockets;

using ModShelf.Sources.DegMods.Catalog;

namespace ModShelf.Sources.DegMods.Downloads;

/// <summary>
/// Where DEG Mods' files are, and how ModShelf takes them from there: a Blossom file server names a file by its SHA-256,
/// so any of them may have it; a few hosts are known to give the file itself at its link; another host's link gives
/// the file when it answers with something other than a web page; and a page, such as MediaFire's, Google Drive's or
/// Mega's, leads to it, which only a browser can follow.
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
    /// Whether the link names this computer or a network it's on, by its name or its address: a name without a dot,
    /// such as <c>localhost</c>, one under <c>.localhost</c> or <c>.local</c>, a loopback, private, link-local or shared
    /// address. ModShelf asks no such link anything, whatever a mod's
    /// author wrote.
    /// </summary>
    public static bool IsOnThisNetwork(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        string host = link.IdnHost.TrimEnd('.');
        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            return !host.Contains('.', StringComparison.Ordinal)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    /// <summary>
    /// Whether an answer to HEAD is a file rather than a web page: a success, with a type other than HTML's.
    /// </summary>
    public static bool IsFile(HttpResponseMessage answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return answer.IsSuccessStatusCode
            && answer.Content.Headers.ContentType?.MediaType is { Length: > 0 } type
            && !type.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
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
