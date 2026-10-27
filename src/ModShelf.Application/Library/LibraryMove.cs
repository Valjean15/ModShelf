namespace ModShelf.Application.Library;

/// <summary>
/// What <see cref="ILibraryMover.MoveAsync"/> did.
/// </summary>
/// <param name="From">The old root.</param>
/// <param name="To">The new root, as it's stored.</param>
/// <param name="Moved">How many files it moved.</param>
/// <param name="AlreadyThere">How many files were under the new root already, from a move that stopped part-way.</param>
/// <param name="Missing">
/// How many files the library has that weren't on disk. They stay in the library, to be downloaded again.
/// </param>
/// <remarks>When the library is in the new root already, nothing changes, and <paramref name="From"/> is <paramref name="To"/>.</remarks>
public sealed record LibraryMove(string From, string To, int Moved, int AlreadyThere, int Missing);
