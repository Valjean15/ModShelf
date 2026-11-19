namespace ModShelf.Cli.Mcp;

/// <summary>
/// What one of the library's archives holds, in a sentence too, a page of its entries, and a text file in it.
/// </summary>
/// <param name="FileName">The archive's name in the library.</param>
/// <param name="Format">zip, 7z or rar.</param>
/// <param name="Files">How many files it holds.</param>
/// <param name="Folders">How many folders.</param>
/// <param name="UnpackedBytes">The files' sizes once unpacked, together.</param>
/// <param name="Readme">The path of its readme, nearest its top; null for none.</param>
/// <param name="Total">How many entries, folders and files, on every page.</param>
/// <param name="Offset">How many of them come before this page.</param>
/// <param name="HasMore">Whether more come after it: ask again with the next offset.</param>
/// <param name="Text">The text file asked for, or else the readme; null for none.</param>
internal sealed record ContentsListing(
    string Message,
    string FileName,
    string Format,
    int Files,
    int Folders,
    long UnpackedBytes,
    string? Readme,
    int Total,
    int Offset,
    bool HasMore,
    IReadOnlyList<ContentsEntry> Entries,
    ContentsText? Text);
