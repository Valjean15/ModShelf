namespace ModShelf.Cli.Mcp;

/// <summary>
/// A text file in an archive, as much of it as was read.
/// </summary>
/// <param name="Entry">Its path in the archive.</param>
/// <param name="Bytes">Its whole size once unpacked.</param>
/// <param name="IsCut">Whether only its start is given.</param>
internal sealed record ContentsText(string Entry, string Text, long Bytes, bool IsCut);
