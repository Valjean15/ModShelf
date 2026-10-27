using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Running what the user asked for, so that nothing it throws reaches the UI.
/// </summary>
internal static class UserCommands
{
    /// <summary>
    /// Runs <paramref name="work"/>, which tells the user of the problems they can act on itself. Cancelling
    /// <paramref name="cancellationToken"/>, as when the app closes, ends it quietly; anything else it throws is logged
    /// with <paramref name="log"/>, and the user is told it didn't work.
    /// </summary>
    public static async Task RunAsync(Func<Task> work, IDialogService dialogs, Action<Exception> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            await work();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception)
        {
            log(exception);
            try
            {
                await dialogs.ShowMessageAsync(Strings.ActionFailedTitle, Strings.SomethingWentWrong, CancellationToken.None);
            }
            catch (Exception shown)
            {
                log(shown);
            }
        }
    }
}
