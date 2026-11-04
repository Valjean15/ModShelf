using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Moves the library to another root folder, with every file it has, and a game to a folder of its own, with its files.
/// </summary>
public interface ILibraryMover
{
    /// <summary>
    /// Moves every file the library has to the same place under <paramref name="root"/>, then makes it the library's
    /// root. Other files under the old root stay, and folders the move empties there are removed. When a file can't be
    /// moved, the ones moved go back, and the root stays. The files of games with folders of their own stay where they
    /// are.
    /// </summary>
    /// <remarks>
    /// A move that stopped part-way, because it was cancelled or the app or the computer stopped, finishes when it's asked
    /// for again: a file already under the new root, and gone from the old one, counts as moved. Until then, the root is
    /// the old one.
    /// </remarks>
    /// <param name="root">The new root, an absolute path.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The new root is inside the library or holds it, is a game's folder, is inside one or holds one, downloads into
    /// the library aren't finished or one started while it moved, a finished one couldn't be added to the library first, another file is where one of the library's
    /// would go, or a file's path there would be too long for this system. The message says which, and whether every
    /// file went back.
    /// </exception>
    /// <exception cref="IOException">
    /// A file couldn't be moved, or the new root couldn't be saved. The message says whether every file went back.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<LibraryMove> MoveAsync(string root, CancellationToken cancellationToken);

    /// <summary>
    /// Says what moving <paramref name="game"/>'s files to <paramref name="folder"/> would do, moving nothing: how many
    /// files and bytes move, from where to where, and the room there.
    /// </summary>
    /// <param name="folder">The game's new folder, an absolute path; null for the library's root.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The folder is the library's root or another game's folder, is inside one or holds one; downloads of the game's
    /// files into its folder aren't finished; another file is where one of the game's would go; or a file's path there
    /// would be too long for this system. The message says which.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<GameMovePlan> PlanGameMoveAsync(GameRef game, string? folder, CancellationToken cancellationToken);

    /// <summary>
    /// Moves <paramref name="game"/>'s files to the same places under <paramref name="folder"/>, then makes it the
    /// game's folder. A file there already, at its size, counts as moved, and a copy of it left in the old folder stays.
    /// Other files in the old folder stay, and folders the move empties there are removed.
    /// </summary>
    /// <remarks>
    /// When a file can't be moved, or the move is cancelled, the files moved go back, and the game's folder stays. A move
    /// that stopped because the app or the computer did finishes when it's asked for again.
    /// </remarks>
    /// <param name="folder">The game's new folder, an absolute path; null for the library's root.</param>
    /// <param name="progress">Told how far the move is, before the first file and after each.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// As for <see cref="PlanGameMoveAsync"/>; or the folder is on another drive with less room than the files take, or
    /// a download into the game's folder started while it moved, or a finished one couldn't be added to the library
    /// first. The message says which, and whether every file went back.
    /// </exception>
    /// <exception cref="IOException">
    /// A file couldn't be moved, or the game's new folder couldn't be saved. The message says whether every file went
    /// back.
    /// </exception>
    /// <exception cref="OperationCanceledException">The move was cancelled; every file it moved went back.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<LibraryMove> MoveGameAsync(GameRef game, string? folder, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken);
}
