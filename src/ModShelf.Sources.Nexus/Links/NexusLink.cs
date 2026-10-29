using System.Collections.Specialized;
using System.Globalization;
using System.Web;

using ModShelf.Domain.Mods;

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

    private const string NotCollections = "ModShelf downloads single files, not collections.";

    private const string NotPages = "That's a link to Nexus Mods' servers, not to a page: a download link from them stops "
        + "working after a while. Copy the mod's page link, or the file's link on its Files tab, instead.";

    // Mod pages are on www. The next. site redirects its game pages there and hosts collections, so its links are
    // read too: a collection gets a clear refusal instead of being fetched as a web page by the direct-link source.
    private static readonly string[] WebsiteHosts = ["nexusmods.com", "www.nexusmods.com", "next.nexusmods.com"];

    // The rest of the site's hosts serve files and the API, under these domains. Their links get a clear refusal too:
    // fetched by the direct-link source, a download link would be saved with its signature, and soon stop working.
    private static readonly string[] ServerDomains = ["nexusmods.com", "nexus-cdn.com"];

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

        if (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp)
        {
            return null;
        }

        if (WebsiteHosts.Contains(link.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            return ReadPage(link);
        }

        return IsServer(link.IdnHost) ? throw new ArgumentException(NotPages, nameof(link)) : null;
    }

    /// <summary>
    /// The link to one of this source's games, such as one a caller asks about, checked as a link the user gives is.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't Nexus Mods', or its name isn't one Nexus Mods gives.</exception>
    public static string ForGame(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Source != NexusSource.NexusId)
        {
            throw new ArgumentException($"The game belongs to source '{game.Source}', not '{NexusSource.NexusId}'.", nameof(game));
        }

        return GameDomain(game.Key) ?? throw new ArgumentException("The game isn't one Nexus Mods names.", nameof(game));
    }

    /// <summary>
    /// The link to one of this source's mods, checked as a link the user gives is.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods', or its game or ID aren't ones Nexus Mods gives.</exception>
    public static NexusLink ForMod(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        string game = ForGame(mod.Game);
        string id = Id(mod.Id) ?? throw new ArgumentException("The mod's ID isn't one Nexus Mods gives.", nameof(mod));
        return new NexusLink(game, id, FileId: null, IsHandoff: false);
    }

    /// <summary>
    /// The link to one of this source's files, such as one a saved download job names, checked as a link the user
    /// gives is.
    /// </summary>
    /// <exception cref="ArgumentException">The file isn't Nexus Mods', or its game or IDs aren't ones Nexus Mods gives.</exception>
    public static NexusLink ForFile(ModFileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);
        NexusLink mod = ForMod(file.Mod);
        string fileId = Id(file.Id) ?? throw new ArgumentException("The file's ID isn't one Nexus Mods gives.", nameof(file));
        return mod with { FileId = fileId };
    }

    /// <summary>
    /// A game's domain name as the API gives it, such as in the list of games, checked as a link the user gives is.
    /// </summary>
    /// <returns>The domain name in lower case, or null when it isn't one Nexus Mods gives.</returns>
    public static string? GameFromApi(string? game) => game is null ? null : GameDomain(game);

    /// <summary>
    /// The link to a mod as the API names it, such as in the account's tracked mods.
    /// </summary>
    /// <returns>The link, or null when the game or ID aren't ones Nexus Mods gives.</returns>
    public static NexusLink? FromApi(string? game, long modId)
        => game is not null && GameDomain(game) is { } domain && modId > 0
            ? new NexusLink(domain, modId.ToString(CultureInfo.InvariantCulture), FileId: null, IsHandoff: false)
            : null;

    private static bool IsServer(string host)
        => ServerDomains.Any(domain => string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>/{game}/mods/{mod}</c>, with the file in the <c>file_id</c> parameter when there is one.
    /// </summary>
    private static NexusLink ReadPage(Uri link)
    {
        string[] path = Segments(link);
        if (path.Contains("collections", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(NotCollections, nameof(link));
        }

        if (path is not [string game, string mods, string mod] || !string.Equals(mods, "mods", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "That Nexus Mods link isn't to a mod. Copy the link of the mod's page, or of a file on its Files tab.",
                nameof(link));
        }

        NameValueCollection query = HttpUtility.ParseQueryString(link.Query);
        string? file = query["file_id"];
        return new NexusLink(GameIn(game, link), IdIn(mod, link), file is null ? null : IdIn(file, link), IsHandoff: false);
    }

    /// <summary>
    /// <c>nxm://{game}/mods/{mod}/files/{file}</c>. The key and expiry in its query stay in the link.
    /// </summary>
    private static NexusLink ReadHandoff(Uri link)
    {
        string[] path = Segments(link);
        if (path is ["collections", ..])
        {
            throw new ArgumentException(NotCollections, nameof(link));
        }

        if (path is not ["mods", string mod, "files", string file])
        {
            throw new ArgumentException("That nxm:// link isn't to a file.", nameof(link));
        }

        return new NexusLink(GameIn(link.Host, link), IdIn(mod, link), IdIn(file, link), IsHandoff: true);
    }

    private static string[] Segments(Uri link)
        => link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string GameIn(string text, Uri link)
        => GameDomain(text) ?? throw new ArgumentException("That Nexus Mods link names no game ModShelf can look up.", nameof(link));

    private static string IdIn(string text, Uri link)
        => Id(text) ?? throw new ArgumentException("That Nexus Mods link has a mod or file ID that isn't a number.", nameof(link));

    /// <summary>
    /// The game's domain name in lower case, or null unless it has only letters, digits, '-' and '_', since it goes
    /// into the API's paths.
    /// </summary>
    private static string? GameDomain(string text)
    {
        string game = text.ToLowerInvariant();
        return game.Length is > 0 and <= MaxGameLength
            && game.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_')
                ? game
                : null;
    }

    /// <summary>
    /// A positive whole number, written without leading zeros, so that the same mod or file always has the same ID.
    /// Null for anything else.
    /// </summary>
    private static string? Id(string text)
        => text.All(char.IsAsciiDigit) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
}
