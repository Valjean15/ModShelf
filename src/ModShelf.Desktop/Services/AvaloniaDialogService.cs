using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Shows dialogs as small windows over the main window, on the UI thread whichever thread asks.
/// </summary>
internal sealed class AvaloniaDialogService : IDialogService
{
    private readonly IBrowserLauncher _browser;

    /// <param name="browser">Opens the pages that help answer a question, such as where to find an API key.</param>
    public AvaloniaDialogService(IBrowserLauncher browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        _browser = browser;
    }

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
        => ShowAsync(new MessageDialogContent(title, message, Strings.DialogOk, null), cancellationToken);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken cancellationToken)
        => ShowAsync(new MessageDialogContent(title, message, confirmLabel, Strings.DialogCancel), cancellationToken);

    public async Task<string?> AskTextAsync(TextQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(question.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(question.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(question.ConfirmLabel);
        cancellationToken.ThrowIfCancellationRequested();

        TextDialogContent content = new(question.Title, question.Message, question.Hint, question.ConfirmLabel, Strings.DialogCancel)
        {
            PasswordChar = question.IsSecret ? '•' : '\0',
            HelpPage = question.HelpPage,
            HelpPageLabel = question.HelpPageLabel,
            OpenPage = page => _browser.TryOpenAsync(page, cancellationToken),
        };
        bool confirmed = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            TextDialog dialog = new() { DataContext = content };
            return await ShowAsync(dialog, () => dialog.Confirmed, cancellationToken);
        });
        return confirmed ? content.Text : null;
    }

    public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        cancellationToken.ThrowIfCancellationRequested();

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // The system's folder picker needs a window to belong to.
            if (AppWindows.Main is not { } owner)
            {
                return null;
            }

            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        });
    }

    private static Task<bool> ShowAsync(MessageDialogContent content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.ConfirmLabel);
        cancellationToken.ThrowIfCancellationRequested();

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            MessageDialog dialog = new() { DataContext = content };
            return await ShowAsync(dialog, () => dialog.Confirmed, cancellationToken);
        });
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> over the main window until it closes, or until
    /// <paramref name="cancellationToken"/> closes it. Runs on the UI thread.
    /// </summary>
    /// <param name="confirmed">Whether the user confirmed, once the dialog has closed.</param>
    private static async Task<bool> ShowAsync(Window dialog, Func<bool> confirmed, CancellationToken cancellationToken)
    {
        await using CancellationTokenRegistration closing = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        if (AppWindows.Main is { } owner)
        {
            await dialog.ShowDialog<bool>(owner);
            return confirmed();
        }

        // Without a window to sit over, such as while ModShelf is only in the tray.
        TaskCompletionSource closed = new();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        await closed.Task;
        return confirmed();
    }
}
