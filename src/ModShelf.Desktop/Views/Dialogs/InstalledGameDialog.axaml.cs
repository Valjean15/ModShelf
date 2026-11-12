using Avalonia.Controls;
using Avalonia.Interactivity;

using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// The dialog choosing an installed game's games on the sites: a choice kept closes it; closing it any other way changes
/// nothing.
/// </summary>
public sealed partial class InstalledGameDialog : Window
{
    private InstalledGamePickViewModel? _picking;

    public InstalledGameDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_picking is not null)
        {
            _picking.CloseRequested -= OnCloseRequested;
        }

        _picking = DataContext as InstalledGamePickViewModel;
        if (_picking is not null)
        {
            _picking.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Ready to type the game's name.
        Picker.FocusField();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
