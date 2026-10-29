using Avalonia.Controls;
using Avalonia.Interactivity;

using ModShelf.Presentation.Resources;

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

    /// <summary>
    /// Opens the page that helps answer; when the browser doesn't open, shows its address to open by hand.
    /// </summary>
    private async void OnOpenHelpPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextDialogContent { HelpPage: { } page, OpenPage: { } open })
        {
            return;
        }

        bool opened;
        try
        {
            opened = await open(page);
        }
        catch (Exception exception) when (exception is ArgumentException or OperationCanceledException)
        {
            // A page the browser launcher won't open, or the dialog closing meanwhile: its address is shown instead.
            opened = false;
        }

        HelpPageProblem.Text = opened ? null : Strings.DialogPageDidntOpen + Environment.NewLine + page.AbsoluteUri;
        HelpPageProblem.IsVisible = !opened;
    }
}
