using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// Which of the library's mods to export: every one, a game's, or the ones chosen; a game and mods chosen together
/// keep the chosen mods of that game.
/// </summary>
/// <param name="Game">The game whose mods to export; null for every game.</param>
/// <param name="Mods">The mods to export; null for every mod.</param>
public sealed record ListScope(GameRef? Game = null, IReadOnlyCollection<ModRef>? Mods = null)
{
    /// <summary>
    /// The whole library.
    /// </summary>
    public static ListScope Library { get; } = new();

    /// <summary>
    /// Whether the scope takes the mod.
    /// </summary>
    public bool Takes(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return (Game is null || mod.Game == Game) && (Mods is null || Mods.Contains(mod));
    }
}
