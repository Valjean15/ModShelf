namespace ModShelf.Application.Settings;

/// <summary>
/// How much of mods' pictures a screen of the desktop app shows.
/// </summary>
public enum PreviewLevel
{
    /// <summary>
    /// None: a compact list of names.
    /// </summary>
    Off,

    /// <summary>
    /// A small picture at the start of each row.
    /// </summary>
    Thumbnails,

    /// <summary>
    /// A grid of cards, each with a large picture.
    /// </summary>
    Cards,
}
