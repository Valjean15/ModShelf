using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// A game of the library, in totals: what a screen shows of the library's games, read without its mods.
/// </summary>
/// <param name="Mods">How many of the library's mods are the game's.</param>
/// <param name="ModsWithFiles">How many of those have files in the library.</param>
/// <param name="SizeInBytes">The room the game's files in the library take, archived versions included.</param>
public sealed record LibraryGameTotals(GameRef Game, int Mods, int ModsWithFiles, long SizeInBytes);
