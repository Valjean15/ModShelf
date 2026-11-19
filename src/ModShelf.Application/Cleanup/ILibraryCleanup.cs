using ModShelf.Domain.Mods;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// Frees up space in the library's folders: lists the files that could go, by group, keeping any that may be the only
/// copy left, and deletes the ones the user chooses (ADR 0104). It never moves or renames anything.
/// </summary>
public interface ILibraryCleanup
{
    /// <summary>
    /// Lists what could go from <paramref name="game"/>'s files, or every game's when it's null. Only files on disk are
    /// listed:
    /// <list type="bullet">
    /// <item>A library file with the same SHA-256 and size as another library file is a duplicate, unless it's the copy
    /// kept (<see cref="CleanupCopy"/>). Duplicates are found across the whole library, so the copy kept may be another
    /// game's.</item>
    /// <item>Any other library file in an <c>_archive</c> folder is an archived old version, and any other file of a mod
    /// that isn't watched is unwatched. Each is kept when it may be the only copy left: its mod is gone from its site,
    /// or the site doesn't offer it, or couldn't be asked.</item>
    /// <item>A file in a mod's folders that the library doesn't have is unmatched when it matches none of the mod's
    /// files on its site, as a rescan matches them; kept otherwise, or when the site couldn't be asked.</item>
    /// </list>
    /// Each mod's site is asked once at most, and only about a mod with archived, unwatched or unknown files. A refused
    /// account or a request to slow down stops asking that site.
    /// </summary>
    /// <param name="progress">Told as each mod's site is asked.</param>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library's folders aren't known.</exception>
    Task<CleanupPlan> PlanAsync(GameRef? game, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes files a plan listed as able to go, one at a time, holding the library still meanwhile. A library file
    /// goes from disk and the library, as deleting it from its mod's page does; an unmatched file goes from disk. Each is
    /// recorded in the library's history, and folders it leaves empty are removed.
    /// <para/>
    /// A file is left, as a failure, when it changed since it was listed: it's not at the same path or of the same size,
    /// it's become the library's or left it, its mod is watched again, or the copy kept of a duplicate changed. So is
    /// one that can't be deleted, such as one another program has open.
    /// </summary>
    /// <param name="progress">Told as each file is done with.</param>
    /// <exception cref="ArgumentException">An entry is kept (<see cref="CleanupEntry.CanGo"/> is false).</exception>
    /// <exception cref="InvalidOperationException">
    /// The library couldn't be held, because a finished download couldn't be recorded. The message says to try again
    /// later.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library's folders aren't known.</exception>
    Task<CleanupResult> DeleteAsync(IReadOnlyList<CleanupEntry> entries, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken);
}
