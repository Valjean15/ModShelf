namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Where the library database file lives. Set by the <c>AddLibraryDatabase</c> methods of <see cref="InfrastructureServiceCollectionExtensions"/>.
/// </summary>
internal sealed record LibraryDatabaseOptions(string DatabasePath);
