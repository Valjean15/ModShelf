using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Dialogs;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Imports a folder's archives: how many there are, what each is once identified, then the copying, then what it
/// did. It closes when the view model asks, or when the user closes it, which, while it works, stops it first.
/// </summary>
public sealed partial class ImportArchivesDialog : Window
{
    private ImportArchivesViewModel? _importing;

    public ImportArchivesDialog()
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

        _importing = DataContext as ImportArchivesViewModel;
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

        // While it identifies or copies, the window's × stops it, and the dialog stays to say what it did.
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
