using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Dialogs;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Frees up space: what could go, listed by group once the sites are asked, the files chosen, then the deleting, then
/// what it did. It closes when the view model asks, or when the user closes it, which, while it works, stops it first.
/// </summary>
public sealed partial class FreeUpSpaceDialog : Window
{
    private FreeUpSpaceViewModel? _freeing;

    public FreeUpSpaceDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_freeing is not null)
        {
            _freeing.CloseRequested -= OnCloseRequested;
        }

        _freeing = DataContext as FreeUpSpaceViewModel;
        if (_freeing is not null)
        {
            _freeing.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_freeing is not { } freeing)
        {
            return;
        }

        // While it lists or deletes, the window's × stops it, and the dialog stays to say what it did.
        if (!freeing.CanClose)
        {
            e.Cancel = true;
            if (freeing.StopCommand.CanExecute(null))
            {
                freeing.StopCommand.Execute(null);
            }

            return;
        }

        freeing.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
