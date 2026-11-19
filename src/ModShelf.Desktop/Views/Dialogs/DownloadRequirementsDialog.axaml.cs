using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.ModPages;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Downloads a mod with what it needs: the requirements, the ones to download ticked, then what came of queueing them.
/// It closes when the view model asks, or when the user closes it.
/// </summary>
public sealed partial class DownloadRequirementsDialog : Window
{
    private DownloadRequirementsViewModel? _downloading;

    public DownloadRequirementsDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_downloading is not null)
        {
            _downloading.CloseRequested -= OnCloseRequested;
        }

        _downloading = DataContext as DownloadRequirementsViewModel;
        if (_downloading is not null)
        {
            _downloading.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_downloading is not null)
        {
            _downloading.CloseRequested -= OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
