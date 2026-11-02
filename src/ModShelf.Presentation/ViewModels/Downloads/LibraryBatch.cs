using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// Several files queued for the library at once, such as a game's updates or the mods selected in Browse, one after
/// the other, and what to say of them.
/// </summary>
internal static partial class LibraryBatch
{
    /// <summary>
    /// Queues each link's file for the library, as Download does for one. One that can't be queued doesn't stop the
    /// others; one that fails unexpectedly is logged.
    /// </summary>
    /// <param name="items">Each file's link, and its mod, which the log names.</param>
    /// <param name="ofUpdates">Whether the files are updates, which is what it says they are; otherwise, files.</param>
    /// <returns>What to say, such as "Queued 2 updates. They show on the Downloads screen.", and whether it's a problem.</returns>
    public static async Task<(string Note, bool IsProblem)> QueueAsync(
        ILibraryDownloads downloads,
        IReadOnlyList<(Uri Link, ModRef Mod)> items,
        bool ofUpdates,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(logger);

        int queued = 0;
        int inLibrary = 0;
        List<string> problems = [];
        foreach ((Uri link, ModRef mod) in items)
        {
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
                LogQueueFailed(logger, exception, mod.Id, mod.Game.Key);
                problems.Add(Strings.SomethingWentWrong);
            }
        }

        List<string> said = [];
        if (queued > 0)
        {
            said.Add(ofUpdates
                ? Counts.Of(queued, Strings.GameUpdatesQueuedOne, Strings.GameUpdatesQueued)
                : Counts.Of(queued, Strings.BatchFilesQueuedOne, Strings.BatchFilesQueued));
        }

        if (inLibrary > 0)
        {
            said.Add(queued == 0 && problems.Count == 0
                ? ofUpdates ? Strings.GameUpdatesInLibrary : Strings.BatchFilesInLibrary
                : Counts.Of(inLibrary, Strings.BatchInLibraryOne, Strings.BatchInLibrary));
        }

        if (problems.Count > 0)
        {
            said.Add((ofUpdates, problems.Count == 1) switch
            {
                (true, true) => Text.Format(Strings.GameUpdatesFailedOne, problems[0]),
                (true, false) => Text.Format(Strings.GameUpdatesFailed, problems.Count, problems[0]),
                (false, true) => Text.Format(Strings.BatchFilesFailedOne, problems[0]),
                (false, false) => Text.Format(Strings.BatchFilesFailed, problems.Count, problems[0]),
            });
        }

        return (string.Join(" ", said), problems.Count > 0);
    }
}
