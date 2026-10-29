using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Following one job until it's done.
/// </summary>
internal static partial class DownloadCommand
{
    /// <summary>
    /// Shows the job's progress and how it ended, and asks for the link that completes its handoff whenever it waits
    /// for one. A link the user pastes is handed to the job through <see cref="ILinkDownloads.HandOverAsync"/>, so the
    /// source checks it. A download into the library is recorded there once it's done.
    /// </summary>
    /// <param name="library">The library the file goes into; or null for a download with --to.</param>
    /// <param name="job">The job as it was read once the watch began.</param>
    /// <param name="logFolder">Where the log files are, to point to when the job fails unexpectedly.</param>
    private sealed class Follow(
        CliEnvironment environment,
        ILinkDownloads links,
        ILibraryDownloads? library,
        HandoffPrompt prompt,
        DownloadJobSnapshot job,
        string? logFolder)
    {
        private const string PasteOrStop = "Paste the link the site handed over, or press Ctrl+C to stop.";

        private readonly ProgressDisplay _display = new(environment.Output, interactive: environment.OutputIsTerminal);
        private Task<string?>? _typed;
        private bool _waitingForHandoff;
        private string? _saved;

        /// <param name="updates">Every job's snapshots, watched since before the job was read.</param>
        /// <param name="stopWatching">Ends <paramref name="updates"/>.</param>
        public async Task<int> RunAsync(
            IAsyncEnumerable<DownloadJobSnapshot> updates,
            CancellationTokenSource stopWatching,
            CancellationToken cancellationToken)
        {
            IAsyncEnumerator<DownloadJobSnapshot> watching = updates.GetAsyncEnumerator(CancellationToken.None);
            Task<bool> next = Task.FromResult(false);
            try
            {
                // The job as it was once the watch began: it may have finished or shown its page already.
                if (await ShowAsync(job, cancellationToken).ConfigureAwait(false) is { } ended)
                {
                    return ended;
                }

                next = watching.MoveNextAsync().AsTask();
                while (true)
                {
                    // Snapshots that have arrived go first, so the user's line meets the job as it is now.
                    if (_typed is { } typed && await Task.WhenAny(next, typed).ConfigureAwait(false) == typed)
                    {
                        _typed = null;
                        if (await typed.ConfigureAwait(false) is not { } line)
                        {
                            return await NoLinkAsync().ConfigureAwait(false);
                        }

                        await HandOffAsync(line, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!await next.ConfigureAwait(false))
                    {
                        break;
                    }

                    DownloadJobSnapshot update = watching.Current;
                    next = watching.MoveNextAsync().AsTask();
                    if (update.Id == job.Id && await ShowAsync(update, cancellationToken).ConfigureAwait(false) is { } exitCode)
                    {
                        return exitCode;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl+C while a pasted link was being handed over, or while the browser was opening.
            }
            finally
            {
                // Ending the watch ends the wait for the next snapshot, so the stream can be closed.
                await stopWatching.CancelAsync().ConfigureAwait(false);
                await next.ConfigureAwait(false);
                await watching.DisposeAsync().ConfigureAwait(false);
            }

            // The updates end when Ctrl+C cancels the token, or when the engine stops on its own.
            _display.Finish();
            if (cancellationToken.IsCancellationRequested && _saved is { } saved)
            {
                // Ctrl+C while the file was being added to the library.
                await environment.Error.WriteLineAsync(
                        $"Stopped. {saved} is saved; it's added to the library the next time a download command runs, or the desktop app starts.")
                    .ConfigureAwait(false);
                return ExitCodes.Interrupted;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return await StoppedAsync(environment, _waitingForHandoff).ConfigureAwait(false);
            }

            await environment.Error.WriteLineAsync("The download engine stopped before the download finished.").ConfigureAwait(false);
            return ExitCodes.Failed;
        }

        /// <returns>The exit code once the job has ended, or null while it goes on.</returns>
        private async Task<int?> ShowAsync(DownloadJobSnapshot update, CancellationToken cancellationToken)
        {
            _display.Show(update);
            _waitingForHandoff = update.Status == DownloadJobStatus.AwaitingHandoff;
            switch (update.Status)
            {
                case DownloadJobStatus.Completed:
                    _display.Finish();
                    _saved = Path.Combine(update.TargetDirectory, update.FileName);
                    environment.Output.WriteLine($"Saved {_saved}");
                    return library is null ? ExitCodes.Done : await RecordAsync(library, update, cancellationToken).ConfigureAwait(false);

                case DownloadJobStatus.Failed:
                    _display.Finish();
                    await environment.Error.WriteLineAsync(FailureMessage(update.FailureReason, logFolder)).ConfigureAwait(false);
                    return ExitCodes.Failed;

                case DownloadJobStatus.Cancelled:
                    _display.Finish();
                    await environment.Error.WriteLineAsync("The download was cancelled.").ConfigureAwait(false);
                    return ExitCodes.Failed;

                // Asked once per wait: while a read is going, the page is on screen already.
                case DownloadJobStatus.AwaitingHandoff when update.HandoffPage is { } page && _typed is null:
                    _display.Finish();
                    _typed = await prompt.AskAsync(page, cancellationToken).ConfigureAwait(false);
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Hands the line the user typed to the job as its link. When it's the link the job waits for, the job resolves
        /// again with it; when it isn't, the user is asked again.
        /// </summary>
        private async Task HandOffAsync(string line, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(line.Trim(), UriKind.Absolute, out Uri? link))
            {
                _typed = prompt.AskAgain($"That isn't a link. {PasteOrStop}");
                return;
            }

            HandoffResult handed;
            try
            {
                handed = await links.HandOverAsync(job.Id, link, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (Problem(exception, library: false) is { } problem)
            {
                _typed = prompt.AskAgain($"{problem.Message} {PasteOrStop}");
                return;
            }

            switch (handed.Answer)
            {
                case HandoffAnswer.Taken:
                    return;
                case HandoffAnswer.NotAHandoff:
                    _typed = prompt.AskAgain(
                        $"That's a link to {job.FileName}, but not the one the site hands over: start the download on the page above, then "
                        + "paste the link its download button holds, or press Ctrl+C to stop.");
                    return;
                default:
                    string other = handed.OtherFileName is { } name ? $"another file, {name}" : "another file";
                    _typed = prompt.AskAgain($"That link is to {other}. Paste the link for {job.FileName}, or press Ctrl+C to stop.");
                    return;
            }
        }

        /// <summary>
        /// Records the finished download in the library, and says what happened to the old versions it replaced.
        /// </summary>
        private async Task<int> RecordAsync(ILibraryDownloads library, DownloadJobSnapshot done, CancellationToken cancellationToken)
        {
            try
            {
                if (await library.RecordAsync(done, cancellationToken).ConfigureAwait(false) is { } recorded)
                {
                    environment.Output.WriteLine("Added to the library.");
                    foreach (LibraryFile archived in recorded.Archived)
                    {
                        environment.Output.WriteLine($"Moved the old version {archived.FileName} to the _archive folder in its folder.");
                    }

                    foreach (LibraryFile deleted in recorded.Deleted)
                    {
                        environment.Output.WriteLine($"Deleted the old version {deleted.FileName}.");
                    }
                }

                return ExitCodes.Done;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whatever went wrong, such as a site that refused the account or a database that couldn't be written,
                // the file is saved, and the user should know that first.
                string reason = exception is ArgumentException refused ? refused.MessageForUser() : exception.Message;
                await environment.Error.WriteLineAsync($"The file is saved, but it couldn't be added to the library: {reason}")
                    .ConfigureAwait(false);
                return ExitCodes.Failed;
            }
        }

        private async Task<int> NoLinkAsync()
        {
            _display.Finish();
            environment.Output.WriteLine();
            await environment.Error.WriteLineAsync(
                    "No link was given, so the download is still waiting to be started on the site. Run the same command "
                    + "again to continue, or run it with the link the site hands over.")
                .ConfigureAwait(false);
            return ExitCodes.Waiting;
        }
    }
}
