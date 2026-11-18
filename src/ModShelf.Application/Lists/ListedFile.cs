using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// A file of a <see cref="ListedMod"/>.
/// </summary>
/// <param name="File">The file, by its site's IDs.</param>
/// <param name="Name">Its name on its site when the list was made.</param>
/// <param name="Version">Its version label, if it had one.</param>
/// <param name="Category">Its section on its site.</param>
/// <param name="Optional">Whether the list marks it optional, as a collection does: it's downloaded only when asked.</param>
public sealed record ListedFile(ModFileRef File, string Name, string? Version, ModFileCategory Category, bool Optional);
