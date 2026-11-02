namespace ModShelf.Application.Settings;

/// <summary>
/// How the desktop app shows the pictures of mods with adult content.
/// </summary>
public enum AdultPictures
{
    /// <summary>
    /// Not at all: a plain placeholder stands in.
    /// </summary>
    Hidden,

    /// <summary>
    /// Blurred: the site's blurred picture when it gives one, so the real one isn't downloaded, or the real one blurred
    /// by the app.
    /// </summary>
    Blurred,

    /// <summary>
    /// As they are.
    /// </summary>
    Shown,
}
