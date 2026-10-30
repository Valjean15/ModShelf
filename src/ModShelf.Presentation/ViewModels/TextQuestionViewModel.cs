using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A question answered with text, typed or pasted, as a dialog asks it; with a page that helps answer, such as where to
/// find an API key.
/// </summary>
public sealed partial class TextQuestionViewModel : ObservableObject
{
    private readonly IBrowserLauncher _browser;

    /// <param name="browser">Opens the page that helps answer.</param>
    public TextQuestionViewModel(TextQuestion question, IBrowserLauncher browser)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(browser);

        _browser = browser;
        Title = question.Title;
        Message = question.Message;
        Hint = question.Hint;
        ConfirmLabel = question.ConfirmLabel;
        IsSecret = question.IsSecret;
        HelpPage = question.HelpPage;
        HelpPageLabel = question.HelpPageLabel;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>
    /// What the box shows while it's empty, such as "API key".
    /// </summary>
    public string Hint { get; }

    public string ConfirmLabel { get; }

    public string CancelLabel { get; } = Strings.DialogCancel;

    /// <summary>
    /// Whether the answer is a secret, such as an API key, which isn't shown as it's typed.
    /// </summary>
    public bool IsSecret { get; }

    /// <summary>
    /// A web page that helps the user answer; null for none.
    /// </summary>
    public Uri? HelpPage { get; }

    public string? HelpPageLabel { get; }

    public bool HasHelpPage => HelpPage is not null && !string.IsNullOrWhiteSpace(HelpPageLabel);

    /// <summary>
    /// What the user typed or pasted.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// The help page's address, to open by hand, when the browser didn't open it; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? HelpPageProblem { get; private set; }

    /// <summary>
    /// Opens the page that helps answer; when the browser doesn't open, shows its address to open by hand.
    /// </summary>
    [RelayCommand]
    private async Task OpenHelpPageAsync(CancellationToken cancellationToken)
    {
        if (!HasHelpPage || HelpPage is not { } page)
        {
            return;
        }

        bool opened;
        try
        {
            opened = await _browser.TryOpenAsync(page, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or OperationCanceledException)
        {
            // A page the browser launcher won't open, or the dialog closing meanwhile: its address is shown instead.
            opened = false;
        }

        HelpPageProblem = opened ? null : Strings.DialogPageDidntOpen + Environment.NewLine + page.AbsoluteUri;
    }
}
