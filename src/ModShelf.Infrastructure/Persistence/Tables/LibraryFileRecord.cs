using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>LibraryFiles</c> table: one of a library mod's files. <see cref="LibraryModRecord"/> maps it to and
/// from <see cref="LibraryFile"/>.
/// </summary>
/// <remarks>A mutable class rather than a record, because EF Core tracks entities by reference.</remarks>
internal sealed class LibraryFileRecord
{
    /// <summary>
    /// The row's key, assigned by SQLite in insertion order. It keeps a mod's files in the order they were added.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The <see cref="LibraryModRecord.Sequence"/> of the mod the file belongs to.
    /// </summary>
    public long ModSequence { get; set; }

    public required string FileId { get; set; }

    public required string Name { get; set; }

    public string? VersionLabel { get; set; }

    public ModFileCategory Category { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>
    /// The file's path relative to the library root, with '/' between folders.
    /// </summary>
    public required string RelativePath { get; set; }

    public long SizeInBytes { get; set; }

    public DateTimeOffset DownloadedAt { get; set; }

    public static LibraryFileRecord From(LibraryFile file)
    {
        LibraryFileRecord record = new() { FileId = file.File.Id, Name = file.Name, RelativePath = file.RelativePath };
        record.CopyFrom(file);
        return record;
    }

    public void CopyFrom(LibraryFile file)
    {
        Name = file.Name;
        VersionLabel = file.VersionLabel;
        Category = file.Category;
        UploadedAt = file.UploadedAt;
        RelativePath = file.RelativePath;
        SizeInBytes = file.SizeInBytes;
        DownloadedAt = file.DownloadedAt;
    }

    /// <exception cref="ArgumentException">The row holds values no library file can have.</exception>
    public LibraryFile ToFile(ModRef mod)
        => new(new ModFileRef(mod, FileId), Name, VersionLabel, Category, UploadedAt, RelativePath, SizeInBytes, DownloadedAt);
}
