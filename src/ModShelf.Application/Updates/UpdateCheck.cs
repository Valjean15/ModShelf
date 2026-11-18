using ModShelf.Domain.Library;

namespace ModShelf.Application.Updates;

/// <summary>
/// What <see cref="IUpdateChecker.CheckAsync"/> did.
/// </summary>
/// <param name="Checked">How many watched mods it checked.</param>
/// <param name="Requests">How many questions it asked the sites.</param>
/// <param name="Found">
/// The updates it found that no check had found before, the newest first within each mod; but a pinned mod's, which
/// aren't offered (<see cref="LibraryMod.OfferedUpdates"/>).
/// </param>
/// <param name="Failures">The games and mods it couldn't check.</param>
public sealed record UpdateCheck(int Checked, int Requests, IReadOnlyList<LibraryUpdate> Found, IReadOnlyList<UpdateCheckFailure> Failures)
{
    /// <summary>
    /// The mods this check found gone from their sites, hidden or removed there, that weren't before, as the library has
    /// them now: the library's copies of their files may be the only ones left. Not failures.
    /// </summary>
    public IReadOnlyList<LibraryMod> Gone { get; init; } = [];

    /// <summary>
    /// The mods gone from their sites that this check found back, as the library has them now.
    /// </summary>
    public IReadOnlyList<LibraryMod> Back { get; init; } = [];
}
