namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A mod a file names, and how it relates to it.
/// </summary>
/// <param name="RelationType">
/// 1 a library built in, 2 optional, 3 required, 4 a tool, 5 incompatible, 6 included.
/// </param>
internal sealed record CurseForgeDependency(long ModId, int RelationType);
