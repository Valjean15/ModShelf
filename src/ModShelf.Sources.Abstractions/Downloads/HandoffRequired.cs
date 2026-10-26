using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Downloads;

/// <summary>
/// The source needs the user to start the download on its website, such as the "Mod Manager Download"
/// button that Nexus Mods shows free accounts. The job waits in AwaitingHandoff, showing the page, until the
/// website hands the app the link that completes the download (a link the source recognizes with
/// <see cref="Links.RecognizedLink.IsHandoff"/>); the engine then resolves the file again with that link.
/// </summary>
public sealed record HandoffRequired : ResolveResult
{
    public HandoffRequired(Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(pageUrl);
        if (!WebUrls.IsHttpOrHttps(pageUrl))
        {
            throw new ArgumentException("The handoff page must be an absolute HTTP or HTTPS URL.", nameof(pageUrl));
        }

        PageUrl = pageUrl;
    }

    /// <summary>
    /// The page to open in the user's browser.
    /// </summary>
    public Uri PageUrl { get; }
}
