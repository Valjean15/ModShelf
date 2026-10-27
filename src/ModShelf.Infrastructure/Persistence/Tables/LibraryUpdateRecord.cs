using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>LibraryUpdates</c> table: an update a check found for a library mod. <see cref="LibraryModRecord"/>
/// maps it to and from <see cref="LibraryUpdate"/>.
/// </summary>
/// <remarks>A mutable class rather than a record, because EF Core tracks entities by reference.</remarks>
internal sealed class LibraryUpdateRecord
{
    /// <summary>
    /// The row's key, assigned by SQLite in insertion order.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The <see cref="LibraryModRecord.Sequence"/> of the mod the update is for.
    /// </summary>
    public long ModSequence { get; set; }

    /// <summary>
    /// Where the update is in the mod's list, from 0, since the list's order is the check's.
    /// </summary>
    public int Position { get; set; }

    public required string FileId { get; set; }

    public required string Name { get; set; }

    public required string FileName { get; set; }

    public string? VersionLabel { get; set; }

    public ModFileCategory Category { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>
    /// The ID of the library file the update replaces, if any.
    /// </summary>
    public string? ReplacesFileId { get; set; }

    public DateTimeOffset FoundAt { get; set; }

    public string? PageUrl { get; set; }

    public static LibraryUpdateRecord From(LibraryUpdate update, int position)
    {
        LibraryUpdateRecord record = new() { FileId = update.File.Id, Name = update.Name, FileName = update.FileName };
        record.CopyFrom(update, position);
        return record;
    }

    public void CopyFrom(LibraryUpdate update, int position)
    {
        Position = position;
        Name = update.Name;
        FileName = update.FileName;
        VersionLabel = update.VersionLabel;
        Category = update.Category;
        UploadedAt = update.UploadedAt;
        ReplacesFileId = update.Replaces?.Id;
        FoundAt = update.FoundAt;
        PageUrl = update.PageUrl?.AbsoluteUri;
    }

    /// <exception cref="ArgumentException">The row holds values no update can have.</exception>
    /// <exception cref="UriFormatException">The page isn't a link.</exception>
    public LibraryUpdate ToUpdate(ModRef mod)
        => new(
            new ModFileRef(mod, FileId),
            Name,
            FileName,
            VersionLabel,
            Category,
            UploadedAt,
            ReplacesFileId is null ? null : new ModFileRef(mod, ReplacesFileId),
            FoundAt,
            PageUrl is null ? null : new Uri(PageUrl, UriKind.Absolute));
}
