namespace ModShelf.Application.Lists;

/// <summary>
/// A listed mod, where it stands, and its files.
/// </summary>
public sealed record PlannedMod(ListedMod Listed, PlannedModState State, IReadOnlyList<PlannedFile> Files);
