namespace ModShelf.Application.Library;

/// <summary>
/// How far a move of a game's files is (<see cref="ILibraryMover.MoveGameAsync"/>).
/// </summary>
/// <param name="Moved">How many files have moved.</param>
/// <param name="Files">How many files move in all.</param>
/// <param name="MovedBytes">How many bytes the files moved take.</param>
/// <param name="Bytes">How many bytes the files that move take in all.</param>
public sealed record LibraryMoveProgress(int Moved, int Files, long MovedBytes, long Bytes);
