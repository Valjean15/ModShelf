namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// Which page of a list an answer holds: from <see cref="Index"/>, <see cref="ResultCount"/> of <see cref="TotalCount"/>.
/// </summary>
internal sealed record CurseForgePagination(int Index, int PageSize, int ResultCount, long TotalCount);
