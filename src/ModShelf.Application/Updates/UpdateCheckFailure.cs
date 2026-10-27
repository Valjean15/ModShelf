using ModShelf.Domain.Mods;

namespace ModShelf.Application.Updates;

/// <summary>
/// A game, or one of its mods, that an update check couldn't check.
/// </summary>
/// <param name="Game">The game.</param>
/// <param name="Mod">The mod, or null when none of the game's mods could be checked.</param>
/// <param name="Reason">Why, as the site or the app said it.</param>
public sealed record UpdateCheckFailure(GameRef Game, ModRef? Mod, string Reason);
