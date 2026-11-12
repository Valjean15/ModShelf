using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Installed;

/// <summary>
/// An installed game's games on a site.
/// </summary>
/// <param name="Games">
/// Picked: the game picked, or none when none was. By name: the site's games of the same name, none, one or several.
/// </param>
/// <param name="Picked">Whether the user picked them, rather than their name matching.</param>
public sealed record SiteMatch(SourceId Site, IReadOnlyList<GameInfo> Games, bool Picked);
