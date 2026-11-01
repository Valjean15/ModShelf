namespace ModShelf.Cli.Mcp;

/// <summary>
/// What a check for updates did: how many mods it checked with how many requests, and the new updates it found.
/// </summary>
internal sealed record UpdateCheckResult(int Checked, int Requests, IReadOnlyList<UpdateFound> Found, IReadOnlyList<UpdateCheckFailureEntry> Failures);
