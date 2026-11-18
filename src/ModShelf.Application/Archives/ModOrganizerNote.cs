namespace ModShelf.Application.Archives;

/// <summary>
/// What Mod Organizer 2 noted of an archive it downloaded, in the <c>.meta</c> file beside it (ADR 0101).
/// </summary>
/// <param name="ModId">The mod's ID on Nexus Mods, digits only.</param>
/// <param name="FileId">The file's ID there, digits only; null when it didn't note one.</param>
/// <param name="ModName">The mod's name, as it noted it.</param>
internal sealed record ModOrganizerNote(string ModId, string? FileId, string? ModName);
