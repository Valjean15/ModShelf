using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// Asks for what linking an account needs in dialogs, and hands the browser step to the site's card. Closing a dialog
/// without answering ends the linking, and nothing changes.
/// </summary>
internal sealed class DialogAuthPrompt : IAuthPrompt
{
    private readonly string _siteName;
    private readonly IDialogService _dialogs;
    private readonly Action<Uri, bool> _showBrowserStep;

    /// <param name="siteName">The site's name, as the dialogs say it.</param>
    /// <param name="showBrowserStep">
    /// Shows that the page to approve ModShelf on should be open, and whether the browser opened. It's called on whatever
    /// thread linking runs on.
    /// </param>
    public DialogAuthPrompt(string siteName, IDialogService dialogs, Action<Uri, bool> showBrowserStep)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(showBrowserStep);

        _siteName = siteName;
        _dialogs = dialogs;
        _showBrowserStep = showBrowserStep;
    }

    private string Title => Text.Format(Strings.AccountLinkTitle, _siteName);

    public Task<string> AskEmailAsync(SourceId source, CancellationToken cancellationToken)
        => AskAsync(
            new TextQuestion(Title, Text.Format(Strings.AccountEmailMessage, _siteName), Strings.AccountEmailHint, Strings.AccountSendCode),
            cancellationToken);

    public Task<string> AskApiKeyAsync(SourceId source, Uri? keyPage, CancellationToken cancellationToken)
        => AskAsync(
            new TextQuestion(
                Title,
                Text.Format(keyPage is null ? Strings.AccountApiKeyMessage : Strings.AccountApiKeyMessageWithPage, _siteName),
                Strings.AccountApiKeyHint,
                Strings.AccountLinkConfirm)
            {
                IsSecret = true,
                HelpPage = keyPage,
                HelpPageLabel = keyPage is null ? null : Strings.AccountApiKeyPage,
            },
            cancellationToken);

    public Task<string> AskEmailCodeAsync(SourceId source, string? sentTo, CancellationToken cancellationToken)
        => AskAsync(
            new TextQuestion(
                Title,
                string.IsNullOrWhiteSpace(sentTo)
                    ? Text.Format(Strings.AccountCodeMessage, _siteName)
                    : Text.Format(Strings.AccountCodeMessageSentTo, _siteName, sentTo),
                Strings.AccountCodeHint,
                Strings.AccountLinkConfirm),
            cancellationToken);

    public void ShowBrowserStep(SourceId source, Uri authorizeUrl, bool browserOpened) => _showBrowserStep(authorizeUrl, browserOpened);

    /// <exception cref="OperationCanceledException">The user closed the dialog without answering.</exception>
    private async Task<string> AskAsync(TextQuestion question, CancellationToken cancellationToken)
        => await _dialogs.AskTextAsync(question, cancellationToken) ?? throw new OperationCanceledException();
}
