using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

using ModShelf.Presentation.ViewModels.Games;

namespace ModShelf.Desktop.Views.Games;

/// <summary>
/// The game picker's field and its list: under the field in a popup, or, in a dialog (<see cref="IsInline"/>), in place.
/// Focusing or clicking the field opens the list.
/// </summary>
public sealed partial class GamePicker : UserControl
{
    /// <summary>
    /// Whether the list stays under the field rather than opening over what's below it, as in a dialog.
    /// </summary>
    public static readonly StyledProperty<bool> IsInlineProperty = AvaloniaProperty.Register<GamePicker, bool>(nameof(IsInline));

    public GamePicker()
    {
        InitializeComponent();
        Field.GotFocus += (_, _) => Picker?.OpenCommand.Execute(null);
        FieldBorder.AddHandler(PointerPressedEvent, (_, _) =>
        {
            Field.Focus();
            Picker?.OpenCommand.Execute(null);
        }, handledEventsToo: true);
    }

    public bool IsInline
    {
        get => GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    private GamePickerViewModel? Picker => DataContext as GamePickerViewModel;

    /// <summary>
    /// Puts the keyboard in the field, as a dialog that opens on the picker does.
    /// </summary>
    public void FocusField() => Field.Focus();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsInlineProperty && change.NewValue is true)
        {
            // The list stays in place, so the popup never opens; it no longer follows the list's IsOpen either, which it would
            // set back to false, forgetting what's typed.
            ListPopup.ClearValue(Popup.IsOpenProperty);
            ((DockPanel)ListPopup.Parent!).Children.Remove(ListPopup);
            InlineList.IsVisible = true;
        }
    }
}
