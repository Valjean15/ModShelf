namespace ModShelf.Application.Cleanup;

/// <summary>
/// How far listing or deleting has got.
/// </summary>
/// <param name="Done">How many are done: mods asked about, or files deleted or not.</param>
/// <param name="Total">How many there are to do.</param>
public sealed record CleanupProgress(int Done, int Total);
