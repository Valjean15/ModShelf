using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Dialogs;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Show contents: what one of the library's archives holds, as a tree, beside the text file chosen in it (ADR 0106).
/// It closes when the view model asks, or when the user closes it.
/// </summary>
public sealed partial class FileContentsDialog : Window
{
    private FileContentsViewModel? _contents;

    public FileContentsDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_contents is not null)
        {
            _contents.CloseRequested -= OnCloseRequested;
        }

        _contents = DataContext as FileContentsViewModel;
        if (_contents is not null)
        {
            _contents.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_contents is not null)
        {
            _contents.CloseRequested -= OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
