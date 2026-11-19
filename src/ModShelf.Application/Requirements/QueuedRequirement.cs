using ModShelf.Application.Library;

namespace ModShelf.Application.Requirements;

/// <summary>
/// A requirement's file asked for, and where it goes.
/// </summary>
/// <param name="Download">The file's place in the library, and its download; none when the library had it by then.</param>
public sealed record QueuedRequirement(PlannedRequirement Requirement, LibraryDownload Download);
