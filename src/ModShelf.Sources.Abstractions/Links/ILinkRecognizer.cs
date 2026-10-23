namespace ModShelf.Sources.Abstractions.Links;

/// <summary>
/// Recognizes web links to this source's files, such as a link the user pastes, and turns them into
/// file references the download engine can queue.
/// </summary>
/// <remarks>
/// When several sources recognize a link, a site-specific match wins over a catch-all one
/// (see <see cref="RecognizedLink.IsCatchAll"/>).
/// </remarks>
public interface ILinkRecognizer : IModSource
{
    /// <returns>
    /// The file the link points to, or null when the link isn't one this source handles.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The link is this source's, but can't be downloaded as it is, for example because it's malformed.
    /// </exception>
    Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken);
}
