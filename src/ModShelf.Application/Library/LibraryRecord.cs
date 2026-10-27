using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// What <see cref="ILibraryDownloads.RecordAsync"/> did: the file it recorded, and what the rule for old versions did
/// with the files that one replaced.
/// </summary>
/// <param name="File">The file as the library has it.</param>
/// <param name="Archived">Old versions moved into an <c>_archive</c> folder, as the library has them now.</param>
/// <param name="Deleted">Old versions deleted from the disk and the library, as they were.</param>
public sealed record LibraryRecord(LibraryFile File, IReadOnlyList<LibraryFile> Archived, IReadOnlyList<LibraryFile> Deleted);
