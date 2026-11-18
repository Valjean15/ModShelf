namespace ModShelf.Application.Lists;

/// <summary>
/// A listed file, and whether the library has it already.
/// </summary>
public sealed record PlannedFile(ListedFile Listed, bool InLibrary);
