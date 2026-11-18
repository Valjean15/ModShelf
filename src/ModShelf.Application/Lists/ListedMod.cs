using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// A mod in a <see cref="ModList"/>: which it is, what the library knew of it, and the files listed.
/// </summary>
/// <param name="Mod">The mod, by its site's IDs.</param>
/// <param name="Name">Its name when the list was made, to show before its site is asked.</param>
/// <param name="Author">Its author, if known.</param>
/// <param name="PageUrl">Its page on its site, if known.</param>
/// <param name="Watched">Whether the library watched it for updates.</param>
/// <param name="Pinned">Whether the library had it pinned at its version.</param>
/// <param name="Note">The note the library kept of it, if any.</param>
/// <param name="Files">Its files, none for a mod only watched.</param>
public sealed record ListedMod(
    ModRef Mod,
    string Name,
    string? Author,
    Uri? PageUrl,
    bool Watched,
    bool Pinned,
    string? Note,
    IReadOnlyList<ListedFile> Files);
