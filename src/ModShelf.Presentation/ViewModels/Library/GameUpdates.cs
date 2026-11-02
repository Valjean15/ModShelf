using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Update all on a game's heading: its mods' updates downloaded into the library, one after the other, and what to say
/// of them.
/// </summary>
internal static partial class GameUpdates
{
    /// <summary>
    /// Queues each update's file for the library. One that can't be queued doesn't stop the others; one that fails
    /// unexpectedly is logged.
    /// </summary>
    /// <returns>What to say, such as "Queued 2 updates. They show on the Downloads screen.", and whether it's a problem.</returns>
    public static async Task<(string Note, bool IsProblem)> QueueAsync(
        ILibraryDownloads downloads,
        IReadOnlyList<LibraryUpdate> updates,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(logger);

        int queued = 0;
        int inLibrary = 0;
        List<string> problems = [];
        foreach (LibraryUpdate update in updates)
        {
            if (update.PageUrl is not { } link)
            {
                continue;
            }

            try
            {
                LibraryDownload placed = await downloads.AddAsync(link, cancellationToken);
                if (placed.Queued is null)
                {
                    inLibrary++;
                }
                else
                {
                    queued++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (LinkProblems.Describe(exception, library: true) is { } problem)
            {
                problems.Add(problem);
            }
            catch (Exception exception)
            {
                LogUpdateFailed(logger, exception, update.File.Id, update.File.Mod.Id);
                problems.Add(Strings.SomethingWentWrong);
            }
        }

        List<string> said = [];
        if (queued > 0)
        {
            said.Add(Counts.Of(queued, Strings.GameUpdatesQueuedOne, Strings.GameUpdatesQueued));
        }
        else if (inLibrary > 0 && problems.Count == 0)
        {
            said.Add(Strings.GameUpdatesInLibrary);
        }

        if (problems.Count > 0)
        {
            said.Add(problems.Count == 1
                ? Text.Format(Strings.GameUpdatesFailedOne, problems[0])
                : Text.Format(Strings.GameUpdatesFailed, problems.Count, problems[0]));
        }

        return (string.Join(" ", said), problems.Count > 0);
    }

    [LoggerMessage(EventId = 944, Level = LogLevel.Error, Message = "Downloading update {FileId} of mod {ModId} for Update all failed")]
    private static partial void LogUpdateFailed(ILogger logger, Exception exception, string fileId, string modId);
}
