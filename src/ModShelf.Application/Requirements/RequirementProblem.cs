namespace ModShelf.Application.Requirements;

/// <summary>
/// A requirement whose file couldn't be queued, and why, as the exception <see cref="Library.ILibraryDownloads.AddAsync"/>
/// threw, for the caller to say in its own words.
/// </summary>
public sealed record RequirementProblem(PlannedRequirement Requirement, Exception Error);
