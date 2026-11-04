using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.Common;

/// <summary>
/// The same game on other sites, found by its name: the screens point it out, and offer to copy its settings, but never
/// merge, link or change a game by themselves.
/// </summary>
internal static class SameNamedGames
{
    /// <summary>
    /// Whether two games' names are the same: ignoring case, and spaces at their ends.
    /// </summary>
    public static bool Same(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The games among <paramref name="named"/> on other sites than <paramref name="game"/>'s, named as it is.
    /// </summary>
    public static IReadOnlyList<GameRef> Of(GameRef game, string name, IEnumerable<KeyValuePair<GameRef, string>> named)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(named);

        return [.. named.Where(other => other.Key.Source != game.Source && Same(other.Value, name)).Select(other => other.Key).Distinct()];
    }

    /// <summary>
    /// Such as "Also on Example Mods", or "Also on Example Mods and mod.io"; empty for none.
    /// </summary>
    public static string AlsoOn(IEnumerable<GameRef> others, ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(sources);

        string[] sites = [.. others.Select(other => other.Source).Distinct().Select(sources.SiteName).Order(StringComparer.CurrentCultureIgnoreCase)];
        return sites.Length switch
        {
            0 => string.Empty,
            1 => Text.Format(Strings.GameAlsoOn, sites[0]),
            _ => Text.Format(Strings.GameAlsoOn, Text.Format(Strings.ListAnd, string.Join(", ", sites[..^1]), sites[^1])),
        };
    }
}
