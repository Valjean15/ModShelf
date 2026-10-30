using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Which file replaced which, as a mod's site says, where that's believable: a file is replaced by one uploaded after it,
/// not before.
/// </summary>
/// <remarks>
/// Nexus Mods lists links that go back in time, from a mod's newest file to an archived older one. Followed, they made the
/// older file an update of the newest, and the newest an old version of the older one, to archive or delete.
/// </remarks>
internal static class Replacements
{
    /// <summary>
    /// The file the site says replaced <paramref name="file"/>, unless the site lists it as uploaded before
    /// <paramref name="file"/>; or null when none did.
    /// </summary>
    /// <param name="listed">The mod's files as its site lists them, by reference.</param>
    public static ModFileRef? Next(ModFile file, IReadOnlyDictionary<ModFileRef, ModFile> listed)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(listed);

        return file.ReplacedBy is { } next && !(listed.TryGetValue(next, out ModFile? replacing) && replacing.UploadedAt < file.UploadedAt)
            ? next
            : null;
    }
}
