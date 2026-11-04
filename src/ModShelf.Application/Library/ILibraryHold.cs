namespace ModShelf.Application.Library;

/// <summary>
/// Keeps the library's files still while it or a game moves: no file is placed in the library or recorded there until
/// the hold ends, so none goes to the old folder after its files have left, or stays there unrecorded.
/// </summary>
internal interface ILibraryHold
{
    /// <summary>
    /// Waits for the files being placed or recorded, records the finished downloads into the library's folders that the
    /// library lacks, then holds every other file until the hold is disposed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A finished download into the library couldn't be recorded, which is logged. The message says to try again later.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<IDisposable> HoldAsync(CancellationToken cancellationToken);
}
