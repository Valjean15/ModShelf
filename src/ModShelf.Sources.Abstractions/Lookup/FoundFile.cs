using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Lookup;

/// <summary>
/// A file the site found by its hash, with the mod it's a file of, as the site describes both.
/// </summary>
public sealed record FoundFile(ModInfo Mod, ModFile File);
