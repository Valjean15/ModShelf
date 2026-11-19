using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.ViewModels.Dialogs;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Free up space… (ADR 0104): what could go from the library's folders, the open game's or every game's, listed in its
/// dialog, which deletes what the user chooses once they say so.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>
    /// Shows the Free up space dialog, which starts listing what could go as it opens. The screens show the library's
    /// changes as the files go.
    /// </summary>
    [RelayCommand]
    private async Task FreeUpSpaceAsync(CancellationToken cancellationToken)
    {
        FreeUpSpaceViewModel freeing = new(_cleanup, OpenGame?.Game, OpenGame?.Name, _sources, _services.Dialogs, _logger);
        freeing.StartListing();
        await _services.Dialogs.FreeUpSpaceAsync(freeing, cancellationToken);
    }
}
