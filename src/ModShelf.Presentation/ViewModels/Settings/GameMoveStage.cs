namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Where moving a game's files stands, in the dialog that moves them.
/// </summary>
public enum GameMoveStage
{
    /// <summary>
    /// Finding what would move.
    /// </summary>
    Planning,

    /// <summary>
    /// Saying what would move, and asking to move it.
    /// </summary>
    Asking,

    /// <summary>
    /// The files don't fit on the new folder's drive.
    /// </summary>
    NoRoom,

    /// <summary>
    /// The files can't move there, such as to a folder inside another game's.
    /// </summary>
    Refused,

    /// <summary>
    /// Moving them.
    /// </summary>
    Moving,

    /// <summary>
    /// The move stopped, by the user or a file that couldn't move, and the files moved went back.
    /// </summary>
    Stopped,

    /// <summary>
    /// They moved, or there was nothing to move.
    /// </summary>
    Done,
}
