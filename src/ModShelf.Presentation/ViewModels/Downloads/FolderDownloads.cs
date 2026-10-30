using ModShelf.Application.Downloads;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// Downloads a link's file into a folder the user picks, outside the library, under the name its site gives it; or, when
/// the link doesn't tell, under a name the user types. The Downloads and Browse screens share it.
/// </summary>
internal sealed class FolderDownloads
{
    private readonly ILinkDownloads _links;
    private readonly IDialogService _dialogs;

    public FolderDownloads(ILinkDownloads links, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(dialogs);

        _links = links;
        _dialogs = dialogs;
    }

    /// <summary>
    /// Asks for the folder, then queues the link's file there, asking for a name when the link doesn't give one.
    /// </summary>
    /// <returns>What the queue did with the link; or null when the user picked no folder, or typed no name.</returns>
    /// <remarks>What <see cref="ILinkDownloads.AddAsync"/> throws is passed on, but for a link that doesn't name its file.</remarks>
    public async Task<LinkDownload?> AddAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (await _dialogs.PickFolderAsync(Strings.ChooseFolderTitle, cancellationToken) is not { } picked)
        {
            return null;
        }

        string folder = Path.TrimEndingDirectorySeparator(picked);
        try
        {
            return await _links.AddAsync(link, folder, null, cancellationToken);
        }
        catch (ArgumentException exception) when (exception.ParamName == "fileName")
        {
            TextQuestion question = new(Strings.NameTheFileTitle, Strings.NameTheFileMessage, Strings.NameTheFileHint, Strings.NameTheFileConfirm);
            return await _dialogs.AskTextAsync(question, cancellationToken) is { } name && !string.IsNullOrWhiteSpace(name)
                ? await _links.AddAsync(link, folder, name.Trim(), cancellationToken)
                : null;
        }
    }
}
