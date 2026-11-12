namespace ModShelf.Cli.Mcp;

/// <summary>
/// A game installed on this computer, and its games on the sites.
/// </summary>
/// <param name="Store">The store it was installed from, such as steam.</param>
/// <param name="Id">Its ID in the store, such as Steam's app ID 489830.</param>
/// <param name="Name">Its name in the store.</param>
/// <param name="Sites">Its games on each site that has one, or was said to have none.</param>
internal sealed record InstalledGameEntry(string Store, string Id, string Name, IReadOnlyList<InstalledSiteEntry> Sites);
