namespace ModShelf.Cli.Mcp;

/// <summary>
/// One group of what could go: how many of its files can, their size, and how many are kept.
/// </summary>
internal sealed record CleanupGroupTotal(string Group, int CanGo, long CanGoBytes, int Kept);
