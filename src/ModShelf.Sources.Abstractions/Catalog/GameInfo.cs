using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// A game as the source describes it.
/// </summary>
public sealed record GameInfo
{
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    public GameInfo(GameRef game, string name, Uri? pageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);

        Game = game;
        Name = name;
        PageUrl = pageUrl;
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
}
