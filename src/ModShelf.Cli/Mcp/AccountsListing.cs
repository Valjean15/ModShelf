namespace ModShelf.Cli.Mcp;

/// <summary>
/// The sites that have accounts, and ModShelf's account on each.
/// </summary>
internal sealed record AccountsListing(IReadOnlyList<AccountEntry> Accounts);
