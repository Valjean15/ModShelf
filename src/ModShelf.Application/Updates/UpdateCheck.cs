using ModShelf.Domain.Library;

namespace ModShelf.Application.Updates;

/// <summary>
/// What <see cref="IUpdateChecker.CheckAsync"/> did.
/// </summary>
/// <param name="Checked">How many watched mods it checked.</param>
/// <param name="Requests">How many questions it asked the sites.</param>
/// <param name="Found">The updates it found that no check had found before, the newest first within each mod.</param>
/// <param name="Failures">The games and mods it couldn't check.</param>
public sealed record UpdateCheck(int Checked, int Requests, IReadOnlyList<LibraryUpdate> Found, IReadOnlyList<UpdateCheckFailure> Failures);
