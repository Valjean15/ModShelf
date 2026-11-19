namespace ModShelf.Application.Cleanup;

/// <summary>
/// What freeing up space deleted, and what it didn't.
/// </summary>
/// <param name="Deleted">The files deleted, as they were listed.</param>
/// <param name="Failures">The files not deleted, and why.</param>
public sealed record CleanupResult(IReadOnlyList<CleanupEntry> Deleted, IReadOnlyList<CleanupFailure> Failures)
{
    /// <summary>
    /// The bytes the deleted files took up.
    /// </summary>
    public long Freed => Deleted.Sum(entry => entry.SizeInBytes);
}
