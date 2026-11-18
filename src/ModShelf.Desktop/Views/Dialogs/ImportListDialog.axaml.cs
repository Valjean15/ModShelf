using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Dialogs;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Imports a list: what importing it would do, then the import with its progress, then what it did. It closes when the
/// view model asks, or when the user closes it, which, while the list is imported, stops the import first.
/// </summary>
public sealed partial class ImportListDialog : Window
{
    private ImportListViewModel? _importing;

    public ImportListDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_importing is not null)
        {
            _importing.CloseRequested -= OnCloseRequested;
        }

        _importing = DataContext as ImportListViewModel;
        if (_importing is not null)
        {
            _importing.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_importing is not { } importing)
        {
            return;
        }

        // While the list is imported, the window's × stops the import, and the dialog stays to say what it did.
        if (!importing.CanClose)
        {
            e.Cancel = true;
            if (importing.StopCommand.CanExecute(null))
            {
                importing.StopCommand.Execute(null);
            }

            return;
        }

        importing.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
