namespace ModShelf.Cli.Mcp;

/// <summary>
/// The updates the checks found.
/// </summary>
/// <param name="CheckedAt">When the watched mods were checked, the longest ago; null when one never was.</param>
internal sealed record UpdateListing(DateTimeOffset? CheckedAt, IReadOnlyList<UpdateFound> Updates);
