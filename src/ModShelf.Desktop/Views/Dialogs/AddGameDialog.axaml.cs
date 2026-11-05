using Avalonia.Controls;
using Avalonia.Interactivity;

using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// Settings' Add a game…: the game picker over the sites' games. Choosing a game, or adding a key typed, closes it;
/// closing it any other way adds nothing.
/// </summary>
public sealed partial class AddGameDialog : Window
{
    private AddGameViewModel? _adding;

    public AddGameDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether the user chose a game or added a key.
    /// </summary>
    public bool Confirmed { get; private set; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_adding is not null)
        {
            _adding.AddRequested -= OnAddRequested;
        }

        _adding = DataContext as AddGameViewModel;
        if (_adding is not null)
        {
            _adding.AddRequested += OnAddRequested;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Ready to type the game's name.
        Picker.FocusField();
    }

    private void OnAddRequested(object? sender, EventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
