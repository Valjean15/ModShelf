using System.Globalization;

namespace ModShelf.Sources.Nexus.Links;

/// <summary>
/// A link to one of Nexus Mods' collections: its page on the website, such as
/// <c>https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx</c>, or the <c>nxm://</c> link its
/// "Download" button hands over, such as <c>nxm://skyrimspecialedition/collections/qdurkx/revisions/12</c>.
/// </summary>
/// <param name="Game">The game's domain name in the API, in lower case.</param>
/// <param name="Slug">The collection's slug, such as <c>qdurkx</c>, in lower case.</param>
/// <param name="Revision">The revision the link names; null for the latest the site published.</param>
internal sealed record NexusCollectionLink(string Game, string Slug, int? Revision)
{
    private const int MaxSlugLength = 64;

    /// <summary>
    /// The collection's page on the website: its latest revision's, whichever revision the link names.
    /// </summary>
    public Uri PageUrl => new(NexusLink.Website, $"games/{Game}/collections/{Slug}");

    /// <summary>
    /// Reads a link the user gives: <c>/games/{game}/collections/{slug}</c> or <c>/{game}/collections/{slug}</c> on the
    /// website, or <c>nxm://{game}/collections/{slug}</c>, each with <c>/revisions/{number}</c> or a tab after it.
    /// </summary>
    /// <returns>The link, or null when it isn't to one of Nexus Mods' collections.</returns>
    /// <exception cref="ArgumentException">The link is to a collection, but its game, slug or revision can't be read.</exception>
    public static NexusCollectionLink? Read(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            return null;
        }

        string[] path = NexusLink.Segments(link);
        if (string.Equals(link.Scheme, NexusLink.HandoffScheme, StringComparison.OrdinalIgnoreCase))
        {
            return path is [string collections, string slug, .. string[] rest] && IsCollections(collections)
                ? Of(link.Host, slug, rest, link)
                : null;
        }

        if (!NexusLink.IsWebsite(link))
        {
            return null;
        }

        return path switch
        {
            [string games, string game, string collections, string slug, .. string[] rest]
                when string.Equals(games, "games", StringComparison.OrdinalIgnoreCase) && IsCollections(collections) => Of(game, slug, rest, link),
            [string game, string collections, string slug, .. string[] rest] when IsCollections(collections) => Of(game, slug, rest, link),
            _ => null,
        };
    }

    private static bool IsCollections(string segment) => string.Equals(segment, "collections", StringComparison.OrdinalIgnoreCase);

    private static NexusCollectionLink Of(string game, string slug, string[] rest, Uri link)
    {
        string domain = NexusLink.GameDomain(game)
            ?? throw new ArgumentException("That Nexus Mods collection link names no game ModShelf can look up.", nameof(link));
        string named = slug.ToLowerInvariant();
        if (named.Length > MaxSlugLength || !named.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("That Nexus Mods collection link doesn't name a collection.", nameof(link));
        }

        int? revision = null;
        if (rest is [string revisions, ..] && string.Equals(revisions, "revisions", StringComparison.OrdinalIgnoreCase))
        {
            revision = rest is [_, string number, ..]
                && number.All(char.IsAsciiDigit)
                && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                && value > 0
                    ? value
                    : throw new ArgumentException("That Nexus Mods collection link names a revision that isn't a number.", nameof(link));
        }

        return new NexusCollectionLink(domain, named, revision);
    }
}
