namespace ModShelf.Cli.Mcp;

/// <summary>
/// What a check for updates did: how many mods it checked with how many requests, the new updates it found, the mods it
/// couldn't check, and the mods it found gone from their sites, or back on them.
/// </summary>
/// <param name="Gone">
/// The mods it found gone from their sites, hidden or removed there, that weren't before: the library's copies of their
/// files may be the only ones left. Not failures; they're asked about again weekly.
/// </param>
/// <param name="Back">The mods gone from their sites that it found back.</param>
internal sealed record UpdateCheckResult(
    int Checked,
    int Requests,
    IReadOnlyList<UpdateFound> Found,
    IReadOnlyList<UpdateCheckFailureEntry> Failures,
    IReadOnlyList<LibraryModEntry> Gone,
    IReadOnlyList<LibraryModEntry> Back);
