using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Finds the library's mods by their links, without asking their sites, which may have removed them.
/// </summary>
internal static class LibraryLinks
{
    /// <summary>
    /// The mod in <paramref name="library"/> whose page <paramref name="link"/> is, or is on, such as a link to one of
    /// its files there; or null when there's none. Scheme, case, a <c>www.</c> and an ending slash don't count.
    /// </summary>
    public static ModRef? ModAt(IEnumerable<LibraryMod> library, Uri link)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(link);

        return IsWebLink(link)
            ? library.FirstOrDefault(mod => mod.PageUrl is { } page && IsWebLink(page) && SamePage(page, link))?.Mod
            : null;
    }

    private static bool IsWebLink(Uri link)
        => link.IsAbsoluteUri && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp);

    private static bool SamePage(Uri page, Uri link)
        => string.Equals(Host(page), Host(link), StringComparison.OrdinalIgnoreCase)
            && string.Equals(page.AbsolutePath.TrimEnd('/'), link.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string Host(Uri link) => link.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? link.Host[4..] : link.Host;
}
