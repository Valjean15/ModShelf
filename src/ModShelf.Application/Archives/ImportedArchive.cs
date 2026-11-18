using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Archives;

/// <summary>
/// An archive copied into the library, as the file it is there.
/// </summary>
/// <param name="Source">Where it was copied from, which is left as it was.</param>
public sealed record ImportedArchive(string Source, ModRef Mod, LibraryFile File);
