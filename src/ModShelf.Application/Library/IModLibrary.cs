using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// The library: the mods the user watches for updates, has downloaded files of, or both.
/// </summary>
public interface IModLibrary
{
    /// <returns>Every mod in the library, in the order they were added.</returns>
    Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken);

    /// <returns>The mod as the library has it, or null when it isn't in the library.</returns>
    Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Watches the mod a link is to, or to one of whose files, for updates: adds it to the library, or watches it again.
    /// Its name, author and page are read from its site, so they're up to date either way.
    /// </summary>
    /// <returns>The mod as the library has it now.</returns>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link, or its site can't describe its mods.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Watches a mod for updates by reference, such as one a search found, as
    /// <see cref="WatchAsync(Uri, CancellationToken)"/> does with a link: adds it to the library, or watches it again,
    /// with its name, author and page read from its site.
    /// </summary>
    /// <returns>The mod as the library has it now.</returns>
    /// <exception cref="ArgumentException">
    /// The mod's source isn't registered or can't describe its mods, or the source doesn't know the mod's game or ID.
    /// </exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    Task<LibraryMod> WatchAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Stops watching the mod a link is to. A mod with downloaded files stays in the library; one without leaves it.
    /// Unwatching a mod that isn't watched changes nothing. A link to a library mod's page, or to one of its files there,
    /// is matched without asking its site, which may have removed it.
    /// </summary>
    /// <inheritdoc cref="WatchAsync(Uri, CancellationToken)" path="/exception"/>
    Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Watches a mod in the library for updates, or stops watching it, without asking its site, so it works offline. As
    /// with <see cref="UnwatchAsync"/>, a mod left unwatched and without files leaves the library. Asking for what the mod
    /// is already changes nothing.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    Task<LibraryChange> SetWatchedAsync(ModRef modRef, bool watched, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one of the library's files from disk, for good, then from the library. A mod left unwatched and without
    /// files leaves the library, and the folders the file leaves empty under the library root are removed. A file that's
    /// already gone from disk only leaves the library.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The library doesn't have the file.</exception>
    /// <exception cref="IOException">The file couldn't be deleted, such as when another program has it open. The library keeps it.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not delete the file. The library keeps it.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library root isn't known.</exception>
    Task<LibraryChange> DeleteFileAsync(ModFileRef file, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets the mod a link is to, as <see cref="ForgetAsync(ModRef, CancellationToken)"/> does. A link to a library
    /// mod's page, or to one of its files there, is matched without asking its site, which may have removed it.
    /// </summary>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    /// <exception cref="InvalidOperationException">
    /// A download of one of the mod's files into the library isn't finished. Nothing changed.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library root isn't known.</exception>
    Task<LibraryChange> ForgetAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a mod out of the library, with its file records and its updates, in one change, so it isn't watched any
    /// more. No file on disk is touched: the mod's files stay where they are, no longer the library's, so moving the
    /// library leaves them behind. Its completed downloads into the library leave the download queue, which would
    /// otherwise record them again.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    /// <exception cref="InvalidOperationException">
    /// A download of one of the mod's files into the library isn't finished, so it would come back once it is. Nothing
    /// changed.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library root isn't known.</exception>
    Task<LibraryChange> ForgetAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Pins a mod in the library at the version it has, or unpins it, without asking its site. A pinned mod's updates are
    /// still found, but aren't offered: <see cref="LibraryMod.OfferedUpdates"/> leaves them out. Asking for what the mod
    /// is already changes nothing.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    Task<LibraryChange> SetPinnedAsync(ModRef modRef, bool pinned, CancellationToken cancellationToken);

    /// <summary>
    /// Pins the mod a link is to, or on whose page it is, or unpins it, as <see cref="SetPinnedAsync(ModRef, bool,
    /// CancellationToken)"/> does. A link to a library mod's page, or to one of its files there, is matched without
    /// asking its site.
    /// </summary>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    Task<LibraryChange> SetPinnedAsync(Uri link, bool pinned, CancellationToken cancellationToken);

    /// <summary>
    /// Skips one of a library mod's updates until a newer one comes, so it isn't offered, or offers it again, without
    /// asking its site. Skipping an update skipped already, or offering one that isn't, changes nothing.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    /// <exception cref="ArgumentException">Skipping a file that isn't one of the mod's updates.</exception>
    Task<LibraryChange> SkipUpdateAsync(ModFileRef update, bool skip, CancellationToken cancellationToken);

    /// <summary>
    /// Skips the update a link is to, such as the link <c>library_updates</c> gives, or offers it again, as
    /// <see cref="SkipUpdateAsync(ModFileRef, bool, CancellationToken)"/> does. A link to one of the library's updates is
    /// matched without asking its site.
    /// </summary>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">
    /// The link is malformed, or is the site's but not to a file; or skipping a file that isn't one of the mod's updates.
    /// </exception>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    Task<LibraryChange> SkipUpdateAsync(Uri link, bool skip, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a note of a library mod, its ends trimmed, in place of the one it had; a null or blank note removes it.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    /// <exception cref="ArgumentException">The note is longer than <see cref="LibraryMod.MaxNoteLength"/>.</exception>
    Task<LibraryChange> SetNoteAsync(ModRef modRef, string? note, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a note of the mod a link is to, or on whose page it is, as <see cref="SetNoteAsync(ModRef, string?,
    /// CancellationToken)"/> does. A link to a library mod's page, or to one of its files there, is matched without
    /// asking its site.
    /// </summary>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">
    /// The link is malformed, or is the site's but not to a mod or a file; or the note is too long.
    /// </exception>
    /// <exception cref="KeyNotFoundException">The mod isn't in the library.</exception>
    Task<LibraryChange> SetNoteAsync(Uri link, string? note, CancellationToken cancellationToken);

    /// <summary>
    /// Every change to the library from now on, made by anything in this app: watching and unwatching, downloads recorded
    /// and their old versions, update checks, deleted files, and forgotten mods. A change that leaves a mod as it was isn't
    /// listed. The stream ends when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// It's subscribed when called, so listing the library after calling it misses nothing. Changes made by another
    /// process, such as the CLI while the desktop app runs, aren't seen.
    /// </remarks>
    IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken);
}
