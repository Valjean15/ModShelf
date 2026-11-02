namespace ModShelf.Application.Pictures;

/// <summary>
/// Mods' pictures, saved on disk so each is downloaded once and shows offline. When the saved pictures take more room
/// than the settings allow, the ones shown least recently go first.
/// </summary>
public interface IPictureCache
{
    /// <summary>
    /// The saved copy of a picture, downloaded first when it isn't saved yet. Callers asking for the same picture at
    /// once share one download.
    /// </summary>
    /// <returns>The copy's path, or null when the picture can't be had now: the site doesn't answer, refuses, or sends
    /// something that isn't a picture, or one too large. Asking again later tries again.</returns>
    /// <exception cref="ArgumentException"><paramref name="picture"/> isn't an absolute HTTP or HTTPS address.</exception>
    Task<string?> GetAsync(Uri picture, CancellationToken cancellationToken);

    /// <returns>How much room the saved pictures take, in bytes.</returns>
    Task<long> GetSizeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes every saved picture; a picture in use now stays. They're downloaded again as they're shown.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
