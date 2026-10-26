using System.Collections.Specialized;
using System.Globalization;
using System.Web;

namespace ModShelf.Sources.Nexus.Links;

/// <summary>
/// A link to a mod or one of its files on Nexus Mods: a page on the website, or an <c>nxm://</c> link the website
/// hands over when the user starts a download there.
/// </summary>
/// <param name="Game">The game's domain name in the API, such as <c>skyrimspecialedition</c>, in lower case.</param>
/// <param name="ModId">The mod's ID, as digits without leading zeros.</param>
/// <param name="FileId">The file's ID, or null for a link to the mod as a whole.</param>
/// <param name="IsHandoff">Whether it's an <c>nxm://</c> link, which completes a download started on the website.</param>
internal sealed record NexusLink(string Game, string ModId, string? FileId, bool IsHandoff)
{
    /// <summary>
    /// The scheme of the links the website hands to mod managers.
    /// </summary>
    public const string HandoffScheme = "nxm";

    private const int MaxGameLength = 100;

    private static readonly string[] WebsiteHosts = ["nexusmods.com", "www.nexusmods.com", "next.nexusmods.com"];

    /// <summary>
    /// Reads a link the user gives: a mod's page, such as <c>https://www.nexusmods.com/skyrimspecialedition/mods/12604</c>;
    /// a file on its Files tab, with <c>?tab=files&amp;file_id=35407</c>; or an <c>nxm://</c> link, such as
    /// <c>nxm://skyrimspecialedition/mods/12604/files/35407?key=…&amp;expires=…</c>.
    /// </summary>
    /// <returns>The link, or null when it isn't Nexus Mods'.</returns>
    /// <exception cref="ArgumentException">The link is Nexus Mods', but to something other than a mod or a file.</exception>
    public static NexusLink? Read(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            return null;
        }

        if (string.Equals(link.Scheme, HandoffScheme, StringComparison.OrdinalIgnoreCase))
        {
            return ReadHandoff(link);
        }

        bool isWebsite = (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            && WebsiteHosts.Contains(link.IdnHost, StringComparer.OrdinalIgnoreCase);
        return isWebsite ? ReadPage(link) : null;
    }

    /// <summary>
    /// <c>/{game}/mods/{mod}</c>, with the file in the <c>file_id</c> parameter when there is one.
    /// </summary>
    private static NexusLink ReadPage(Uri link)
    {
        string[] path = Segments(link);
        if (path is not [string game, string mods, string mod] || !string.Equals(mods, "mods", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "That Nexus Mods link isn't to a mod. Copy the link of the mod's page, or of a file on its Files tab.",
                nameof(link));
        }

        NameValueCollection query = HttpUtility.ParseQueryString(link.Query);
        string? file = query["file_id"];
        return new NexusLink(GameDomain(game, link), Id(mod, link), file is null ? null : Id(file, link), IsHandoff: false);
    }

    /// <summary>
    /// <c>nxm://{game}/mods/{mod}/files/{file}</c>. The key and expiry in its query stay in the link.
    /// </summary>
    private static NexusLink ReadHandoff(Uri link)
    {
        string[] path = Segments(link);
        if (path is ["collections", ..])
        {
            throw new ArgumentException("ModShelf downloads single files, not collections.", nameof(link));
        }

        if (path is not ["mods", string mod, "files", string file])
        {
            throw new ArgumentException("That nxm:// link isn't to a file.", nameof(link));
        }

        return new NexusLink(GameDomain(link.Host, link), Id(mod, link), Id(file, link), IsHandoff: true);
    }

    private static string[] Segments(Uri link)
        => link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Letters, digits, '-' and '_' only, since the name goes into the API's paths.
    /// </summary>
    private static string GameDomain(string text, Uri link)
    {
        string game = text.ToLowerInvariant();
        if (game.Length is 0 or > MaxGameLength
            || !game.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException("That Nexus Mods link names no game ModShelf can look up.", nameof(link));
        }

        return game;
    }

    /// <summary>
    /// A positive whole number, written without leading zeros, so that the same mod or file always has the same ID.
    /// </summary>
    private static string Id(string text, Uri link)
        => text.All(char.IsAsciiDigit) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException("That Nexus Mods link has a mod or file ID that isn't a number.", nameof(link));
}
