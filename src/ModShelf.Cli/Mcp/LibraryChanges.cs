namespace ModShelf.Cli.Mcp;

/// <summary>
/// The files downloaded into the library from <paramref name="Since"/> until <paramref name="Until"/>, the oldest first.
/// </summary>
/// <param name="Until">Now: the time to ask from next.</param>
/// <param name="OldVersions">What <c>library.oldVersions</c> does with a file's old versions.</param>
internal sealed record LibraryChanges(DateTimeOffset Since, DateTimeOffset Until, string OldVersions, IReadOnlyList<NewLibraryFile> Files);
