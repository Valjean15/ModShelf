using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// What moving a game's files to another folder would do (<see cref="ILibraryMover.PlanGameMoveAsync"/>).
/// </summary>
/// <param name="Game">The game.</param>
/// <param name="From">The game's folder now: its own, or the library's root.</param>
/// <param name="To">The folder it would have, as it would be stored.</param>
/// <param name="Files">How many files would move.</param>
/// <param name="Bytes">How many bytes those files take.</param>
/// <param name="AlreadyThere">How many files are in the new folder already, at their size, and would count as moved.</param>
/// <param name="Missing">How many of the game's files are in neither folder. They stay in the library, to be downloaded again.</param>
/// <param name="FreeSpace">The room on the new folder's drive; null when the system can't tell.</param>
/// <param name="SameDrive">Whether both folders are on one drive, where files move without being copied.</param>
/// <param name="CopiesKept">Of those already there, how many still have a copy in the old folder, which would stay.</param>
/// <remarks>When the game is in that folder already, nothing would move, and <paramref name="From"/> is <paramref name="To"/>.</remarks>
public sealed record GameMovePlan(
    GameRef Game,
    string From,
    string To,
    int Files,
    long Bytes,
    int AlreadyThere,
    int Missing,
    long? FreeSpace,
    bool SameDrive,
    int CopiesKept = 0)
{
    /// <summary>
    /// Whether the files fit where they'd go: on the same drive, on one with room enough, or on one whose room isn't
    /// known. A move that doesn't fit is refused.
    /// </summary>
    public bool Fits => SameDrive || FreeSpace is not { } free || free >= Bytes;
}
