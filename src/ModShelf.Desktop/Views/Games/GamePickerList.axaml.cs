using System.ComponentModel;

using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Games;

namespace ModShelf.Desktop.Views.Games;

/// <summary>
/// The game picker's list. The game the arrow keys move to scrolls into view.
/// </summary>
public sealed partial class GamePickerList : UserControl
{
    private GamePickerViewModel? _picker;

    public GamePickerList()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_picker is not null)
        {
            _picker.PropertyChanged -= OnPickerChanged;
        }

        _picker = DataContext as GamePickerViewModel;
        if (_picker is not null)
        {
            _picker.PropertyChanged += OnPickerChanged;
        }
    }

    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GamePickerViewModel.Highlighted) && _picker?.Highlighted is { } highlighted)
        {
            EntriesList.ContainerFromItem(highlighted)?.BringIntoView();
        }
    }
}
