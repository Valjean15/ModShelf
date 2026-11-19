namespace ModShelf.Cli.Mcp;

/// <summary>
/// A folder or a file in an archive.
/// </summary>
/// <param name="Path">Where it is in the archive, with '/' between folders.</param>
/// <param name="Bytes">Its size once unpacked; 0 for a folder.</param>
internal sealed record ContentsEntry(string Path, long Bytes, bool IsFolder);
