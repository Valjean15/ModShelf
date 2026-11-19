using ModShelf.Domain.Mods;

namespace ModShelf.Application.Contents;

/// <summary>
/// What one of the library's archives holds, read where it is, without extracting anything (ADR 0106).
/// </summary>
/// <param name="FileName">The archive's name in the library, such as <c>SkyUI 5.2.7z</c>.</param>
/// <param name="Format">Its kind: <c>zip</c>, <c>7z</c> or <c>rar</c>.</param>
/// <param name="Entries">
/// Its folders and files, each folder listed once, those only implied by their files' paths too, ordered by path, a
/// folder before what's in it.
/// </param>
/// <param name="Files">How many files, folders left out.</param>
/// <param name="UnpackedSize">The files' sizes once unpacked, together.</param>
/// <param name="Readme">The path of the entry that's the archive's readme, the one nearest its top; null for none.</param>
public sealed record ArchiveContents(
    ModFileRef File,
    string FileName,
    string Format,
    IReadOnlyList<ArchiveEntry> Entries,
    int Files,
    long UnpackedSize,
    string? Readme);
