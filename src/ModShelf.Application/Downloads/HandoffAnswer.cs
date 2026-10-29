namespace ModShelf.Application.Downloads;

/// <summary>
/// What <see cref="ILinkDownloads.HandOverAsync"/> made of a link given for a download waiting to be started on its
/// site.
/// </summary>
public enum HandoffAnswer
{
    /// <summary>The link the site handed over for the file: the download goes on with it.</summary>
    Taken,

    /// <summary>
    /// A link to the file, such as its page, but not the one the site hands over once the download is started there.
    /// </summary>
    NotAHandoff,

    /// <summary>A link to another file. Nothing was added for it.</summary>
    OtherFile,
}
