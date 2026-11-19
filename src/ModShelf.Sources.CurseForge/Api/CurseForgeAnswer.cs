namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// What every answer of CurseForge's Core API holds: its data, and for a list, which page of it.
/// </summary>
internal sealed record CurseForgeAnswer<T>(T? Data, CurseForgePagination? Pagination);
