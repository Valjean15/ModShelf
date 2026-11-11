using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Taking the file a user's browser downloaded for a job waiting on a site's page (ADR 0091): the job is verified and
/// put in place as a downloaded one is, and waits on its page again when the file isn't the one.
/// </summary>
internal sealed partial class DownloadScheduler
{
    public async Task<TakeFileOutcome> TakeFileAsync(DownloadJobId id, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' isn't a full path.", nameof(path));
        }

        TaskCompletionSource<TakeFileOutcome> reply = NewReply<TakeFileOutcome>();
        Post(new TakeFileCommand(id, path, reply));
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts checking and moving the file, outside the loop, with the job verifying meanwhile; its page is kept, for
    /// when the file isn't the one.
    /// </summary>
    private async Task TakeFileAsync(TakeFileCommand command)
    {
        DownloadJob job = _jobs.Find(job => job.Id == command.Id)
            ?? throw new KeyNotFoundException($"There is no download job {command.Id}.");
        if (job.Status != DownloadJobStatus.AwaitingHandoff)
        {
            throw new InvalidOperationException($"The download job is {job.Status}: only one waiting on a site's page takes a file.");
        }

        job.TakeFile();
        TakingFile taking = new(_handoffPages.GetValueOrDefault(job.Id), command.Reply);
        _taking[job.Id] = taking;
        await SaveAsync(job).ConfigureAwait(false);

        ModFileRef file = job.File;
        string destination = DestinationOf(job);
        CancellationToken cancellationToken = taking.Cancellation.Token;
        _ = Task.Run(
            async () =>
            {
                TakeFileOutcome outcome = await TakeAsync(file, command.Path, destination, cancellationToken).ConfigureAwait(false);
                Post(new FileTaken(command.Id, outcome));
            },
            CancellationToken.None);
    }

    private async Task<TakeFileOutcome> TakeAsync(ModFileRef file, string path, string destination, CancellationToken cancellationToken)
    {
        try
        {
            FileHash? expected = SiteFileHashes.Best(await SiteFileHashes.OfAsync(_sources, file, cancellationToken).ConfigureAwait(false));
            return await _transfer.TakeAsync(path, expected, destination, cancellationToken).ConfigureAwait(false) switch
            {
                Ports.TransferOutcome.Completed => TakeFileOutcome.Taken,
                Ports.TransferOutcome.HashMismatch => TakeFileOutcome.HashMismatch,
                _ => TakeFileOutcome.StorageError,
            };
        }
        catch (FileNotFoundException)
        {
            return TakeFileOutcome.NotFound;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            LogTakeFailed(_logger, exception, Path.GetFileName(path));
            return TakeFileOutcome.StorageError;
        }
    }

    /// <summary>
    /// The take ended: the job completes, or waits on its page again, unless it was cancelled meanwhile.
    /// </summary>
    private async Task OnFileTakenAsync(FileTaken report)
    {
        if (!_taking.Remove(report.Id, out TakingFile? taking))
        {
            return;
        }

        using (taking)
        {
            if (_jobs.Find(job => job.Id == report.Id) is { Status: DownloadJobStatus.Verifying } job)
            {
                if (report.Outcome == TakeFileOutcome.Taken)
                {
                    job.Complete();
                    LogFileTaken(_logger, job.Id);
                }
                else
                {
                    job.RefuseTakenFile();
                    if (taking.Page is { } page)
                    {
                        _handoffPages[job.Id] = page;
                    }

                    LogFileRefused(_logger, job.Id, report.Outcome);
                }

                await SaveAsync(job).ConfigureAwait(false);
            }

            taking.Reply.TrySetResult(report.Outcome);
        }
    }

    /// <param name="Path">Left out of <see cref="ToString"/>: it can hold the user's name.</param>
    private sealed record TakeFileCommand(DownloadJobId Id, string Path, TaskCompletionSource<TakeFileOutcome> Reply) : Command
    {
        public override void Reject(Exception exception) => Reply.TrySetException(exception);

        public override string ToString() => $"{nameof(TakeFileCommand)} {{ Id = {Id} }}";
    }

    private sealed record FileTaken(DownloadJobId Id, TakeFileOutcome Outcome) : Report(Id);

    /// <summary>
    /// A file being taken for a job: the page the job waited on, and who asked.
    /// </summary>
    private sealed class TakingFile(Uri? page, TaskCompletionSource<TakeFileOutcome> reply) : IDisposable
    {
        public Uri? Page { get; } = page;

        public TaskCompletionSource<TakeFileOutcome> Reply { get; } = reply;

        public CancellationTokenSource Cancellation { get; } = new();

        public void Dispose() => Cancellation.Dispose();
    }
}
