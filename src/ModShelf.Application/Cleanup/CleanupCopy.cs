using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// The copy of a duplicate that's kept: not in an <c>_archive</c> folder when one isn't, then a watched mod's, then the
/// one downloaded last.
/// </summary>
/// <param name="Path">Where it is, an absolute path.</param>
/// <param name="Mod">The mod it's a file of.</param>
/// <param name="ModName">The mod's name.</param>
/// <param name="File">The file, as the library has it.</param>
public sealed record CleanupCopy(string Path, ModRef Mod, string ModName, LibraryFile File);
