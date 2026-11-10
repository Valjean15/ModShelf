namespace ModShelf.Sources.GameBanana.Links;

/// <summary>
/// A link to one of GameBanana's mods, or to a file of one: the mod's page (<c>gamebanana.com/mods/&lt;mod&gt;</c>), its
/// download page (<c>…/mods/download/&lt;mod&gt;</c>), or a 1-click link (<c>gamebanana.com/mmdl/&lt;file&gt;,Mod,&lt;mod&gt;</c>),
/// as it is or behind a mod manager's scheme, as GameBanana's 1-Click Install button gives it to one.
/// </summary>
/// <param name="ModId">The mod's ID.</param>
/// <param name="FileId">The file's ID, or null for the mod as a whole.</param>
internal sealed record GameBananaLink(string ModId, string? FileId)
{
    /// <summary>
    /// GameBanana's host.
    /// </summary>
    public const string Host = "gamebanana.com";

    private const string NoMod =
        "This GameBanana link names a file but not its mod, so ModShelf can't tell whose it is. Copy the mod's page link instead.";

    private const string NotAMod = "This GameBanana link isn't to a mod or its files. Copy a mod's page link.";

    // Schemes whose links are never fetched, whatever follows them.
    private static readonly string[] NeverFetched = ["file", "javascript", "mailto", "data", "vbscript", "about"];

    /// <summary>
    /// The mod's page on GameBanana.
    /// </summary>
    public static Uri ModPage(string modId) => new($"https://{Host}/mods/{modId}");

    /// <summary>
    /// A file's 1-click link, naming its mod: GameBanana's own link for one file, which it gives no page. Opened in a
    /// browser, it downloads the file.
    /// </summary>
    public static Uri FileLink(string modId, string fileId) => new($"https://{Host}/mmdl/{fileId},Mod,{modId}");

    /// <summary>
    /// The link's mod and file, or null when it isn't GameBanana's.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The link is GameBanana's, but to neither a mod nor a file of one, or to a file without saying its mod.
    /// </exception>
    public static GameBananaLink? Read(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (WebLink(link) is not { } web || !IsGameBanana(web.Host))
        {
            return null;
        }

        return web.AbsolutePath.Trim('/').ToLowerInvariant().Split('/') switch
        {
            ["mods", var mod] when IsId(mod) => new GameBananaLink(mod, null),
            ["mods", "download", var mod] when IsId(mod) => new GameBananaLink(mod, null),
            ["mmdl", var oneClick] => OneClick(oneClick),
            ["dl", var file] when IsId(file) => throw new ArgumentException(NoMod, nameof(link)),
            _ => throw new ArgumentException(NotAMod, nameof(link)),
        };
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a web link, written with its scheme or as GameBanana's host and a path, and if so
    /// the member whose profile it's to, as in <c>https://gamebanana.com/members/1740038</c>.
    /// </summary>
    /// <param name="memberId">The member's ID; null when the link is to anything else, or anywhere else.</param>
    /// <returns>False for text that isn't a link, such as a member's name.</returns>
    public static bool TryReadMember(string text, out string? memberId)
    {
        ArgumentNullException.ThrowIfNull(text);

        memberId = null;
        string written = text.Trim();
        if (written.StartsWith($"{Host}/", StringComparison.OrdinalIgnoreCase) || written.StartsWith($"www.{Host}/", StringComparison.OrdinalIgnoreCase))
        {
            written = $"https://{written}";
        }

        if (!Uri.TryCreate(written, UriKind.Absolute, out Uri? link) || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        if (IsGameBanana(link.Host) && link.AbsolutePath.Trim('/').ToLowerInvariant().Split('/') is ["members", var member, ..] && IsId(member))
        {
            memberId = member;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is an ID as GameBanana gives them: a number.
    /// </summary>
    public static bool IsId(string? text)
        => text is { Length: > 0 and <= 18 } && text.All(char.IsAsciiDigit);

    /// <summary>
    /// A 1-click link's file and mod: <c>&lt;file&gt;,Mod,&lt;mod&gt;</c>.
    /// </summary>
    private static GameBananaLink OneClick(string what)
        => what.Split(',') switch
        {
            [var file, var kind, var mod] when IsId(file) && IsId(mod) && kind.Equals("Mod", StringComparison.OrdinalIgnoreCase)
                => new GameBananaLink(mod, file),
            [var file] when IsId(file) => throw new ArgumentException(NoMod, nameof(what)),
            _ => throw new ArgumentException(NotAMod, nameof(what)),
        };

    /// <summary>
    /// The web link <paramref name="link"/> is, or carries behind a mod manager's scheme, as in
    /// <c>jasm-plus:https://gamebanana.com/mmdl/…</c>; null for anything else.
    /// </summary>
    private static Uri? WebLink(Uri link)
    {
        if (!link.IsAbsoluteUri || NeverFetched.Contains(link.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
        {
            return link;
        }

        string carried = link.OriginalString[(link.OriginalString.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return Uri.TryCreate(carried, UriKind.Absolute, out Uri? web)
            && (web.Scheme == Uri.UriSchemeHttps || web.Scheme == Uri.UriSchemeHttp)
            && IsGameBanana(web.Host)
            && web.AbsolutePath.StartsWith("/mmdl/", StringComparison.OrdinalIgnoreCase)
                ? web
                : null;
    }

    private static bool IsGameBanana(string host)
        => host.Equals(Host, StringComparison.OrdinalIgnoreCase) || host.Equals($"www.{Host}", StringComparison.OrdinalIgnoreCase);
}
