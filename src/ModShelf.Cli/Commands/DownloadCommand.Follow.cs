using ModShelf.Application.Downloads;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Following one job until it's done.
/// </summary>
internal static partial class DownloadCommand
{
    /// <summary>
    /// Shows the job's progress and how it ended, and asks for the link that completes its handoff whenever it waits
    /// for one. A link the user pastes goes through <see cref="ILinkDownloads"/>, as the same command run with that
    /// link would, so the source checks it.
    /// </summary>
    /// <param name="job">The job as it was read once the watch began.</param>
    /// <param name="fileName">The name the user chose with --name, if any.</param>
    private sealed class Follow(
        CliEnvironment environment,
        IDownloadService downloads,
        ILinkDownloads links,
        HandoffPrompt prompt,
        DownloadJobSnapshot job,
        string? fileName)
    {
        private const string PasteOrStop = "Paste the link the site handed over, or press Ctrl+C to stop.";

        private readonly ProgressDisplay _display = new(environment.Output, interactive: environment.OutputIsTerminal);
        private Task<string?>? _typed;
        private bool _waitingForHandoff;

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
                    environment.Output.WriteLine($"Saved {Path.Combine(update.TargetDirectory, update.FileName)}");
                    return ExitCodes.Done;

                case DownloadJobStatus.Failed:
                    _display.Finish();
                    await environment.Error.WriteLineAsync(FailureMessage(update.FailureReason)).ConfigureAwait(false);
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
        /// Hands the line the user typed to the queue as a link. When it's the link the job waits for, the job resolves
        /// again with it; when it isn't, the user is asked again.
        /// </summary>
        private async Task HandOffAsync(string line, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(line.Trim(), UriKind.Absolute, out Uri? link))
            {
                _typed = prompt.AskAgain($"That isn't a link. {PasteOrStop}");
                return;
            }

            LinkDownload handed;
            try
            {
                handed = await links.AddAsync(link, job.TargetDirectory, fileName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (LinkProblem(exception) is { } problem)
            {
                _typed = prompt.AskAgain($"{problem.Message} {PasteOrStop}");
                return;
            }

            if (handed.Job.Id == job.Id)
            {
                return;
            }

            // The link was to another file. A job added just now for it isn't what the user asked for.
            if (!handed.AlreadyQueued)
            {
                try
                {
                    await downloads.CancelAsync(handed.Job.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // It finished already.
                }
            }

            _typed = prompt.AskAgain($"That link is to another file, {handed.Job.FileName}. Paste the link for {job.FileName}, or press Ctrl+C to stop.");
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
