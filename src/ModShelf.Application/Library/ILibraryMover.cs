namespace ModShelf.Application.Library;

/// <summary>
/// Moves the library to another root folder, with every file it has.
/// </summary>
public interface ILibraryMover
{
    /// <summary>
    /// Moves every file the library has to the same place under <paramref name="root"/>, then makes it the library's
    /// root. Other files under the old root stay, and folders the move empties there are removed. When a file can't be
    /// moved, the ones moved go back, and the root stays.
    /// </summary>
    /// <remarks>
    /// A move that stopped part-way, because it was cancelled or the app or the computer stopped, finishes when it's asked
    /// for again: a file already under the new root, and gone from the old one, counts as moved. Until then, the root is
    /// the old one.
    /// </remarks>
    /// <param name="root">The new root, an absolute path.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The new root is inside the library or holds it, downloads into the library aren't finished or one started while
    /// it moved, a finished one couldn't be added to the library first, another file is where one of the library's
    /// would go, or a file's path there would be too long for this system. The message says which, and whether every
    /// file went back.
    /// </exception>
    /// <exception cref="IOException">
    /// A file couldn't be moved, or the new root couldn't be saved. The message says whether every file went back.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<LibraryMove> MoveAsync(string root, CancellationToken cancellationToken);
}
