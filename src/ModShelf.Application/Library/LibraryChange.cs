using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// What a change did to a mod in the library.
/// </summary>
/// <param name="Before">The mod as it was, or null when it wasn't in the library.</param>
/// <param name="After">The mod as it is now, or null when it isn't in the library any more.</param>
public sealed record LibraryChange(ModRef Mod, LibraryMod? Before, LibraryMod? After);
