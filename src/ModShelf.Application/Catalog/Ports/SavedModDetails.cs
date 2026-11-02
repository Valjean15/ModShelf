using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Catalog.Ports;

/// <summary>
/// A mod's details as saved, and when they were read from its site.
/// </summary>
public sealed record SavedModDetails(ModInfo Details, DateTimeOffset SavedAt);
