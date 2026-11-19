namespace ModShelf.Application.Requirements;

/// <summary>
/// What queueing requirements' files did.
/// </summary>
/// <param name="Queued">Each requirement whose file was asked for, in order, with what came of it.</param>
/// <param name="Problems">Those that couldn't be queued.</param>
public sealed record RequirementDownloads(IReadOnlyList<QueuedRequirement> Queued, IReadOnlyList<RequirementProblem> Problems);
