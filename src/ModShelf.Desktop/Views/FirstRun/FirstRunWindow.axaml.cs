using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.FirstRun;

namespace ModShelf.Desktop.Views.FirstRun;

/// <summary>
/// The first-run setup, over the main window. It closes when the setup is finished or skipped; closing it otherwise
/// leaves the setup to show again at the next start.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    private FirstRunViewModel? _setup;

    public FirstRunWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_setup is not null)
        {
            _setup.CloseRequested -= OnCloseRequested;
        }

        _setup = DataContext as FirstRunViewModel;
        if (_setup is not null)
        {
            _setup.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_setup is not null)
        {
            _setup.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
