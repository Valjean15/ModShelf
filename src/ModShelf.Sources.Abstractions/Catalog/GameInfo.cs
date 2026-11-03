using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// A game as the source describes it.
/// </summary>
public sealed record GameInfo
{
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    /// <param name="categories">The game's mod categories by name; blank and repeated ones are left out.</param>
    public GameInfo(GameRef game, string name, Uri? pageUrl = null, IEnumerable<string>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);

        Game = game;
        Name = name;
        PageUrl = pageUrl;
        Categories =
        [
            .. (categories ?? [])
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim())
                .Distinct(StringComparer.Ordinal),
        ];
    }

    public GameRef Game { get; }

    /// <summary>
    /// The name to show, such as "Stardew Valley".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The game's page on the site, or null when the site has none.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// The categories the site sorts the game's mods into, by name, in the site's order, for a search by category
    /// (<see cref="Search.SearchQuery.Category"/>); empty when the site says none.
    /// </summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Whether both describe the same game the same way, the categories compared one by one rather than as lists.
    /// </summary>
    public bool Equals(GameInfo? other)
        => other is not null
            && Game == other.Game
            && Name == other.Name
            && PageUrl == other.PageUrl
            && Categories.SequenceEqual(other.Categories, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Game, Name, PageUrl, Categories.Count);
}
