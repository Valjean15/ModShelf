namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>SkippedUpdates</c> table: an update of a library mod the user skipped
/// (<see cref="Domain.Library.LibraryMod.SkippedUpdates"/>).
/// </summary>
internal sealed class SkippedUpdateRecord
{
    public long ModSequence { get; set; }

    public required string FileId { get; set; }
}
