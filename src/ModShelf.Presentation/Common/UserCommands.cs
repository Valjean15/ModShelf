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

    /// <summary>
    /// Runs <paramref name="work"/>, which says how it went on the screen, such as in a note beside its button, rather than
    /// in a dialog. Cancelling <paramref name="cancellationToken"/>, as when the app closes, ends it quietly. A problem
    /// <paramref name="describe"/> words, one the user can act on, is shown with <paramref name="showProblem"/>; anything
    /// else <paramref name="work"/> throws is logged with <paramref name="log"/>, and shown as something that went wrong.
    /// </summary>
    /// <param name="describe">What to tell the user of a problem; null for one that isn't theirs to act on.</param>
    public static async Task RunInPlaceAsync(
        Func<Task> work,
        Func<Exception, string?> describe,
        Action<string> showProblem,
        Action<Exception> log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(showProblem);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            await work();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (describe(exception) is { } problem)
        {
            showProblem(problem);
        }
        catch (Exception exception)
        {
            log(exception);
            showProblem(Strings.SomethingWentWrong);
        }
    }
}
