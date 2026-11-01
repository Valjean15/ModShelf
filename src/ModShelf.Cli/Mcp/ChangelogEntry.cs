namespace ModShelf.Cli.Mcp;

/// <summary>
/// A version's changes, as a mod's changelog lists them.
/// </summary>
internal sealed record ChangelogEntry(string Version, IReadOnlyList<string> Changes);
