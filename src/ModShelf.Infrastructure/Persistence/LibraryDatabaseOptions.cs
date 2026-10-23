namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Where the library database file lives. Set by <see cref="InfrastructureServiceCollectionExtensions.AddLibraryDatabase"/>.
/// </summary>
internal sealed record LibraryDatabaseOptions(string DatabasePath);
