using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// A question answered with one of a few buttons. Closing it any other way, or Escape, chooses nothing.
/// </summary>
public sealed partial class ChoiceDialog : Window
{
    public ChoiceDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The index of the button chosen; null for none.
    /// </summary>
    public int? Chosen { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChoiceDialogButton choice })
        {
            Chosen = choice.Index;
            Close();
        }
    }
}
