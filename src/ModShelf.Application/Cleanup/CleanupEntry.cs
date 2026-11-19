using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// A file in the library's folders that could go, to free up space, or that's kept, and why (ADR 0104).
/// </summary>
/// <param name="Group">Why it could go.</param>
/// <param name="Path">Where it is, an absolute path.</param>
/// <param name="RelativePath">Where it is in its game's folder, as a library path.</param>
/// <param name="SizeInBytes">Its size on disk when it was listed.</param>
/// <param name="Mod">
/// The mod it's a file of; for an <see cref="CleanupGroup.Unmatched"/> file, the mod whose folder it's in, the first by
/// name when several mods share the folder.
/// </param>
/// <param name="ModName">The mod's name.</param>
/// <param name="File">The file, as the library has it; null for an <see cref="CleanupGroup.Unmatched"/> file.</param>
/// <param name="SameAs">For a <see cref="CleanupGroup.Duplicates"/> file, the copy that's kept.</param>
/// <param name="Kept">Why it's kept; null when it can go.</param>
/// <param name="KeptDetail">More about why it's kept, as <see cref="KeptBecause"/> says; null otherwise.</param>
public sealed record CleanupEntry(
    CleanupGroup Group,
    string Path,
    string RelativePath,
    long SizeInBytes,
    ModRef Mod,
    string ModName,
    LibraryFile? File = null,
    CleanupCopy? SameAs = null,
    KeptBecause? Kept = null,
    string? KeptDetail = null)
{
    /// <summary>
    /// Whether it can go: it isn't kept.
    /// </summary>
    public bool CanGo => Kept is null;

    /// <summary>
    /// Its name on disk.
    /// </summary>
    public string FileName => RelativePath[(RelativePath.LastIndexOf(LibraryPaths.Separator) + 1)..];
}
