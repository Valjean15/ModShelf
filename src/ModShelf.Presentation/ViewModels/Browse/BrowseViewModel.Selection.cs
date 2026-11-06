using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Selecting mods among those listed, and downloading each one's newest main file into the library. A new search or
/// listing starts with none selected; Load more keeps what's selected.
/// </summary>
public sealed partial class BrowseViewModel
{
    // While the selection changes in bulk, or is being shown, the rows' changes don't show it again each time.
    private bool _showingSelection;

    /// <summary>
    /// Such as "3 selected".
    /// </summary>
    [ObservableProperty]
    public partial string SelectionText { get; private set; } = Text.Format(Strings.SelectionCount, 0);

    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    /// <summary>
    /// Such as "Download selected (3)".
    /// </summary>
    [ObservableProperty]
    public partial string DownloadSelectedText { get; private set; } = Text.Format(Strings.BrowseDownloadSelected, 0);

    /// <summary>
    /// Whether mods are selected, while the last Download selected isn't still queuing them.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadSelectedCommand))]
    public partial bool CanDownloadSelected { get; private set; }

    /// <summary>
    /// Whether Download selected is queuing the selected mods' files.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloadingSelected { get; private set; }

    /// <summary>
    /// Whether every mod listed that can be selected is, none is, or some are (null). Setting it selects or unselects
    /// each of them.
    /// </summary>
    [ObservableProperty]
    public partial bool? AreAllShownSelected { get; set; } = false;

    /// <summary>
    /// What the last Download selected did, such as "Queued 3 files. They show on the Downloads screen."; null before
    /// any.
    /// </summary>
    [ObservableProperty]
    public partial string? SelectionNote { get; private set; }

    [ObservableProperty]
    public partial bool IsSelectionNoteAProblem { get; private set; }

    /// <summary>
    /// Queues the newest main file of each mod selected for the library, in the order listed, then says what it did and
    /// unselects them. A mod whose file the library has is said to be there; one that can't be queued, with why.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadSelected))]
    private async Task DownloadSelectedAsync(CancellationToken cancellationToken)
    {
        BrowseModViewModel[] selected = [.. Results.Where(row => row.IsSelected)];
        IEnumerable<(SourceId, int)> needs = selected.Select(row => (row.Mod.Game.Source, QuotaChecks.PerDownload));
        if (!await QuotaChecks.GoOnAsync(_services.Sources, _services.Dialogs, _services.TimeProvider, needs, cancellationToken))
        {
            return;
        }

        IsDownloadingSelected = true;
        ShowSelection();
        (string Note, bool IsProblem) done = (string.Empty, false);
        try
        {
            done = await LibraryBatch.QueueAsync(
                _services.LibraryDownloads,
                [.. selected.Select(row => (row.Details.PageUrl!, row.Mod))],
                ofUpdates: false,
                _logger,
                cancellationToken);
        }
        finally
        {
            IsDownloadingSelected = false;
            SelectionNote = done.Note;
            IsSelectionNoteAProblem = done.IsProblem;
            Unselect(selected);
        }
    }

    [RelayCommand]
    private void ClearSelection() => Unselect([.. Results]);

    private void Unselect(IEnumerable<BrowseModViewModel> rows)
    {
        _showingSelection = true;
        try
        {
            foreach (BrowseModViewModel row in rows)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            _showingSelection = false;
        }

        ShowSelection();
    }

    partial void OnAreAllShownSelectedChanged(bool? value)
    {
        if (_showingSelection || value is null)
        {
            return;
        }

        _showingSelection = true;
        try
        {
            foreach (BrowseModViewModel row in Results.Where(row => row.CanSelect))
            {
                row.IsSelected = value.Value;
            }
        }
        finally
        {
            _showingSelection = false;
        }

        ShowSelection();
    }

    /// <summary>
    /// Counts the selected mods among those listed. Runs on the UI thread.
    /// </summary>
    private void ShowSelection()
    {
        if (_showingSelection)
        {
            return;
        }

        int selectable = Results.Count(row => row.CanSelect);
        int selected = Results.Count(row => row.IsSelected);
        SelectionText = Text.Format(Strings.SelectionCount, selected);
        HasSelection = selected > 0;
        DownloadSelectedText = Text.Format(Strings.BrowseDownloadSelected, selected);
        CanDownloadSelected = selected > 0 && !IsDownloadingSelected;
        _showingSelection = true;
        try
        {
            AreAllShownSelected = selected == 0 ? false : selected == selectable ? true : null;
        }
        finally
        {
            _showingSelection = false;
        }
    }
}
