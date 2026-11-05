using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Moves a game's files: what would move, then the move with its progress. It closes when the view model asks, once the
/// files moved, or when the user closes it, which, while the files move, stops the move first.
/// </summary>
public sealed partial class GameMoveDialog : Window
{
    private GameMoveViewModel? _moving;

    public GameMoveDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_moving is not null)
        {
            _moving.CloseRequested -= OnCloseRequested;
        }

        _moving = DataContext as GameMoveViewModel;
        if (_moving is not null)
        {
            _moving.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_moving is not { } moving)
        {
            return;
        }

        // While the files move, the window's × stops the move, and the dialog stays to say how it ended.
        if (!moving.CanClose)
        {
            e.Cancel = true;
            if (moving.StopCommand.CanExecute(null))
            {
                moving.StopCommand.Execute(null);
            }

            return;
        }

        // Closed by its ×: nothing more is planned.
        moving.CloseRequested -= OnCloseRequested;
        if (moving.CloseCommand.CanExecute(null))
        {
            moving.CloseCommand.Execute(null);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
