using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Shows dialogs as small windows over the main window, on the UI thread whichever thread asks.
/// </summary>
internal sealed class AvaloniaDialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
        => ShowAsync(new MessageDialogContent(title, message, Strings.DialogOk, null), cancellationToken);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken cancellationToken)
        => ShowAsync(new MessageDialogContent(title, message, confirmLabel, Strings.DialogCancel), cancellationToken);

    /// <summary>
    /// The main window, when the app has one open.
    /// </summary>
    private static Window? Owner()
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } window
            ? window
            : null;

    private static Task<bool> ShowAsync(MessageDialogContent content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.ConfirmLabel);
        cancellationToken.ThrowIfCancellationRequested();

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            MessageDialog dialog = new() { DataContext = content };
            await using CancellationTokenRegistration closing = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            if (Owner() is { } owner)
            {
                return await dialog.ShowDialog<bool>(owner);
            }

            // Without a window to sit over, such as while ModShelf is only in the tray.
            TaskCompletionSource<bool> closed = new();
            dialog.Closed += (_, _) => closed.TrySetResult(dialog.Confirmed);
            dialog.Show();
            return await closed.Task;
        });
    }
}
