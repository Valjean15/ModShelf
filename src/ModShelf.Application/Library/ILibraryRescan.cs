namespace ModShelf.Application.Library;

/// <summary>
/// Finds the files on disk that the library doesn't know, such as ones placed by hand, and reads the hashes the library
/// lacks. It never moves, renames or deletes anything.
/// </summary>
public interface ILibraryRescan
{
    /// <summary>
    /// Rescans the mod a link is to, or every mod in the library when <paramref name="link"/> is null. For each mod:
    /// <list type="bullet">
    /// <item>It looks in the mod's folders: where its files are, and where its game's folder pattern puts new ones, each
    /// with its <c>_archive</c> folder.</item>
    /// <item>It matches each file there the library doesn't know to one of the mod's files on its site, by name (as the
    /// library saves it, or with the file's ID added) and by size, within 1 KB. A match goes into the library, with its
    /// SHA-256; a mod that wasn't in the library comes in, watched, as a download brings one in.</item>
    /// <item>It reads the SHA-256 of the library's files that have none, and says which are missing from disk.</item>
    /// </list>
    /// The site is asked only about a mod with files to match, once, or twice for a mod that isn't in the library.
    /// </summary>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library root isn't known.</exception>
    Task<LibraryRescan> RescanAsync(Uri? link, CancellationToken cancellationToken);
}
