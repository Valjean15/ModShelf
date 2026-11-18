namespace ModShelf.Application.Archives;

/// <summary>
/// An archive in the folder to import, and what its note or its name says of it, before any site is asked.
/// </summary>
/// <param name="Path">Its full path.</param>
/// <param name="Clue"><see cref="ArchiveClue.Notes"/> or <see cref="ArchiveClue.Name"/> when either names its mod; otherwise null.</param>
/// <param name="ModId">The mod its note or name gives.</param>
/// <param name="FileId">The file its note gives, if any.</param>
public sealed record ScannedArchive(string Path, long SizeInBytes, ArchiveClue? Clue, string? ModId, string? FileId)
{
    /// <summary>
    /// Its file name.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(Path);
}
