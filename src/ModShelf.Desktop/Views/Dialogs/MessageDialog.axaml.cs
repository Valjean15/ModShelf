using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// A message, or a question to confirm, with one or two buttons. Closing it any other way turns it down.
/// </summary>
public sealed partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether the user chose the confirming button.
    /// </summary>
    public bool Confirmed { get; private set; }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
