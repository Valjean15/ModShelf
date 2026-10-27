using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// A question answered with text, typed or pasted. Closing it any other way than with the answering button cancels it.
/// </summary>
public sealed partial class TextDialog : Window
{
    public TextDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether the user answered with the confirming button.
    /// </summary>
    public bool Confirmed { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Ready to paste into.
        Answer.Focus();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
