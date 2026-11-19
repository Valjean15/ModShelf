namespace ModShelf.Application.Contents;

/// <summary>
/// A text file in an archive, as much of it as is read (ADR 0106).
/// </summary>
/// <param name="Entry">Its path in the archive.</param>
/// <param name="Text">Its text, or as much as was read.</param>
/// <param name="SizeInBytes">Its whole size once unpacked.</param>
/// <param name="IsCut">Whether only its start was read, the file being larger than ModShelf reads.</param>
public sealed record ArchiveText(string Entry, string Text, long SizeInBytes, bool IsCut);
