using ModShelf.Sources.Thunderstore.Catalog;

namespace ModShelf.Sources.Thunderstore.Links;

/// <summary>
/// A link to a Thunderstore package or one of its versions, as its website and its "Install with Mod Manager" button
/// give them:
/// <list type="bullet">
/// <item><c>https://thunderstore.io/c/{community}/p/{team}/{name}/</c>, a package's page in a community, or one of its
/// tabs;</item>
/// <item><c>https://thunderstore.io/c/{community}/p/{team}/{name}/v/{version}/</c>, a version's page;</item>
/// <item><c>https://thunderstore.io/package/download/{team}/{name}/{version}/</c>, a version's download link;</item>
/// <item><c>https://thunderstore.io/package/{team}/{name}/</c>, the website's old page of a package, on its own host or a
/// community's, such as <c>valheim.thunderstore.io</c>;</item>
/// <item><c>ror2mm://v1/install/thunderstore.io/{team}/{name}/{version}/</c>, the link scheme's.</item>
/// </list>
/// The last three name no community.
/// </summary>
/// <param name="Community">The community the link is in, lowercase; null when it names none.</param>
/// <param name="Version">The version the link is to; null for the package's newest.</param>
/// <param name="IsHandoff">Whether it's a <c>ror2mm://</c> link.</param>
internal sealed record ThunderstoreLink(string? Community, PackageName Package, string? Version, bool IsHandoff)
{
    /// <summary>
    /// Thunderstore's link scheme, which its "Install with Mod Manager" button opens.
    /// </summary>
    public const string Scheme = "ror2mm";

    /// <summary>
    /// Thunderstore's website, whose links ModShelf gives as pages.
    /// </summary>
    public static readonly Uri Website = new("https://thunderstore.io/");

    private const string Host = "thunderstore.io";

    /// <summary>
    /// The page of <paramref name="package"/> in <paramref name="community"/>, or of its version
    /// <paramref name="version"/>.
    /// </summary>
    public static Uri Page(string community, PackageName package, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        string page = $"c/{Uri.EscapeDataString(community)}/p/{Uri.EscapeDataString(package.Team)}/{Uri.EscapeDataString(package.Name)}/";
        return new Uri(Website, version is null ? page : $"{page}v/{Uri.EscapeDataString(version)}/");
    }

    /// <summary>
    /// The website's old page of <paramref name="package"/>, which names no community.
    /// </summary>
    public static Uri PackagePage(PackageName package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new Uri(Website, $"package/{Uri.EscapeDataString(package.Team)}/{Uri.EscapeDataString(package.Name)}/");
    }

    /// <summary>
    /// The community's page on the website.
    /// </summary>
    public static Uri CommunityPage(string community) => new(Website, $"c/{Uri.EscapeDataString(community)}/");

    /// <summary>
    /// The link <paramref name="link"/> is; null when it isn't Thunderstore's, such as a file on its CDN.
    /// </summary>
    /// <exception cref="ArgumentException">The link is Thunderstore's website's, but not to a package or a version.</exception>
    public static ThunderstoreLink? Parse(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            return null;
        }

        if (link.Scheme == Scheme)
        {
            return Install(link);
        }

        if ((link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) || !IsWebsite(link.Host, out bool main))
        {
            return null;
        }

        string[] path = Segments(link);
        ThunderstoreLink? parsed = path switch
        {
            ["c", var community, "p", var team, var name, "v", var version, ..] => Of(community, team, name, version),
            ["c", var community, "p", var team, var name, ..] => Of(community, team, name, null),
            ["package", "download", var team, var name, var version] => Of(null, team, name, version),
            ["package", var team, var name] => Of(null, team, name, null),
            ["package", var team, var name, var version] when PackageName.IsVersion(version) => Of(null, team, name, version),
            _ => null,
        };

        // A link elsewhere on the website, such as a community's page, is Thunderstore's but not to a package; on another
        // of its hosts, such as its CDN, it's no link of the website's.
        return parsed is not null || !main
            ? parsed
            : throw new ArgumentException("That Thunderstore link isn't to a package or one of its versions.", nameof(link));
    }

    private static ThunderstoreLink Install(Uri link)
    {
        string[] path = Segments(link);
        return (link.Host, path) switch
        {
            ("v1", ["install", Host, var team, var name, var version]) => Of(null, team, name, version) with { IsHandoff = true },
            _ => throw new ArgumentException("That ror2mm:// link isn't to a version of a package on thunderstore.io.", nameof(link)),
        };
    }

    private static ThunderstoreLink Of(string? community, string team, string name, string? version)
    {
        if (community is not null && (community.Length == 0 || !community.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException($"'{community}' isn't a Thunderstore community.", nameof(community));
        }

        if (version is not null && !PackageName.IsVersion(version))
        {
            throw new ArgumentException($"'{version}' isn't a version number Thunderstore gives, such as 1.0.0.", nameof(version));
        }

        PackageName package = PackageName.Parse($"{team}-{name}") is { } parsed && parsed.Team == team
            ? parsed
            : throw new ArgumentException($"'{team}/{name}' isn't a Thunderstore package.", nameof(name));
        return new ThunderstoreLink(community?.ToLowerInvariant(), package, version, IsHandoff: false);
    }

    private static string[] Segments(Uri link)
        => [.. link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString)];

    /// <summary>
    /// Whether the host is Thunderstore's website, and whether it's its main one rather than a community's.
    /// </summary>
    private static bool IsWebsite(string host, out bool main)
    {
        main = string.Equals(host, Host, StringComparison.OrdinalIgnoreCase) || string.Equals(host, $"www.{Host}", StringComparison.OrdinalIgnoreCase);
        return main || host.EndsWith($".{Host}", StringComparison.OrdinalIgnoreCase);
    }
}
