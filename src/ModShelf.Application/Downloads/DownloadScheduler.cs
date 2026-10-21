using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Runs the download queue: starts queued jobs within the concurrency limits, drives each one through
/// resolving, transferring and verifying, and carries out pause, resume, retry and cancel requests.
/// </summary>
/// <remarks>
/// <para>
/// All job state belongs to one loop that handles messages in order: commands from callers and reports
/// from running downloads. Only that loop changes or saves jobs, so no locks are needed and every saved
/// state is consistent. The downloads themselves (resolving and transferring) run outside the loop and
/// report back through it.
/// </para>
/// <para>
/// Register one instance as both <see cref="IDownloadService"/> and a hosted service. On start it requeues
/// jobs that were interrupted mid-download; on stop it requeues the running ones, so they continue next time.
/// </para>
/// </remarks>
public sealed partial class DownloadScheduler : BackgroundService, IDownloadService
{
    private readonly IDownloadJobRepository _repository;
    private readonly ISourceRegistry _sources;
    private readonly IFileTransfer _transfer;
    private readonly TimeProvider _timeProvider;
    private readonly DownloadSchedulerOptions _options;
    private readonly ILogger<DownloadScheduler> _logger;

    private readonly Channel<Message> _inbox =
        Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });

    private readonly DownloadWatchers _watchers = new();

    // Owned by the loop: only read or changed while it handles a message.
    private readonly List<DownloadJob> _jobs = [];
    private readonly Dictionary<DownloadJobId, RunningJob> _running = [];
    private readonly Dictionary<DownloadJobId, long> _lastProgressSnapshot = [];
    private bool _stopping;

    public DownloadScheduler(
        IDownloadJobRepository repository,
        ISourceRegistry sources,
        IFileTransfer transfer,
        TimeProvider timeProvider,
        DownloadSchedulerOptions options,
        ILogger<DownloadScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentDownloads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentDownloadsPerSource);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxLinkRefreshes);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ProgressInterval, TimeSpan.Zero);

        _repository = repository;
        _sources = sources;
        _transfer = transfer;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<DownloadJobSnapshot> EnqueueAsync(
        ModFileRef file,
        string targetDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Created here so that invalid arguments throw straight back to the caller.
        DownloadJob job = DownloadJob.Create(file, targetDirectory, fileName, _timeProvider.GetUtcNow());

        TaskCompletionSource<DownloadJobSnapshot> reply = NewReply<DownloadJobSnapshot>();
        Post(new EnqueueCommand(job, reply));
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<IReadOnlyList<DownloadJobSnapshot>> reply = NewReply<IReadOnlyList<DownloadJobSnapshot>>();
        Post(new ListCommand(reply));
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync(id, JobControl.Pause, cancellationToken);

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync(id, JobControl.Resume, cancellationToken);

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync(id, JobControl.Retry, cancellationToken);

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync(id, JobControl.Cancel, cancellationToken);

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken)
        => _watchers.Watch(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await LoadJobsAsync().ConfigureAwait(false);
            await StartQueuedJobsAsync().ConfigureAwait(false);

            try
            {
                while (true)
                {
                    Message message = await _inbox.Reader.ReadAsync(stoppingToken).ConfigureAwait(false);
                    await HandleAsync(message).ConfigureAwait(false);
                    await StartQueuedJobsAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await StopRunningJobsAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            CloseInbox();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // BackgroundService skips ExecuteAsync entirely when the host stops before it got to run. Nothing
        // else would then answer pending requests, so close the inbox here unless the loop is still busy.
        if (ExecuteTask is null || ExecuteTask.IsCompleted)
        {
            CloseInbox();
        }
    }

    /// <summary>
    /// Refuses all further messages, fails the requests still waiting, and ends every watch stream.
    /// Safe to call more than once, but never while the loop is still reading.
    /// </summary>
    private void CloseInbox()
    {
        _inbox.Writer.TryComplete();
        while (_inbox.Reader.TryRead(out Message? leftover))
        {
            (leftover as Command)?.Reject(new InvalidOperationException("The download scheduler has stopped."));
        }

        _watchers.Complete();
    }

    private static TaskCompletionSource<T> NewReply<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string DestinationOf(DownloadJob job)
        => Path.Combine(job.TargetDirectory, job.FileName);

    private async Task ControlAsync(DownloadJobId id, JobControl control, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        TaskCompletionSource reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new ControlCommand(id, control, reply));
        await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Post(Message message)
    {
        if (!_inbox.Writer.TryWrite(message))
        {
            throw new InvalidOperationException("The download scheduler has stopped.");
        }
    }

    private async Task LoadJobsAsync()
    {
        _jobs.AddRange(await _repository.ListAsync(CancellationToken.None).ConfigureAwait(false));

        int requeued = 0;
        foreach (DownloadJob job in _jobs)
        {
            if (job.Status is DownloadJobStatus.Resolving or DownloadJobStatus.Downloading or DownloadJobStatus.Verifying)
            {
                job.Requeue();
                await _repository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                requeued++;
            }
        }

        if (requeued > 0)
        {
            _logger.RequeuedInterruptedJobs(requeued);
        }
    }

    private async Task StartQueuedJobsAsync()
    {
        if (_stopping)
        {
            return;
        }

        foreach (DownloadJob job in _jobs.Where(job => job.Status == DownloadJobStatus.Queued).ToList())
        {
            if (_running.Count >= _options.MaxConcurrentDownloads)
            {
                return;
            }

            SourceId source = job.File.Mod.Game.Source;
            if (_running.Values.Count(running => running.Source == source) >= _options.MaxConcurrentDownloadsPerSource)
            {
                continue;
            }

            job.StartResolving();
            IDownloadResolver? resolver = _sources.Find<IDownloadResolver>(source);
            if (resolver is null)
            {
                _logger.NoResolver(job.Id, source);
                job.Fail(DownloadFailureReason.LinkUnavailable);
                await SaveAsync(job).ConfigureAwait(false);
                continue;
            }

            await SaveAsync(job).ConfigureAwait(false);
            Start(job, resolver);
        }
    }

    private async Task StopRunningJobsAsync()
    {
        _stopping = true;
        foreach (RunningJob running in _running.Values)
        {
            running.RequestStop(StopReason.Shutdown, waiter: null);
        }

        // Keep handling messages until every running download has reported back and been requeued.
        while (_running.Count > 0)
        {
            Message message = await _inbox.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            await HandleAsync(message).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(Message message)
    {
        try
        {
            switch (message)
            {
                case EnqueueCommand command:
                    await EnqueueAsync(command).ConfigureAwait(false);
                    break;
                case ListCommand command:
                    command.Reply.TrySetResult([.. _jobs.Select(DownloadJobSnapshot.Of)]);
                    break;
                case ControlCommand command:
                    await ControlAsync(command).ConfigureAwait(false);
                    break;
                case TransferStarted report:
                    await OnTransferStartedAsync(report).ConfigureAwait(false);
                    break;
                case TransferAdvanced report:
                    OnTransferAdvanced(report);
                    break;
                case TransferVerifying report:
                    await OnTransferVerifyingAsync(report).ConfigureAwait(false);
                    break;
                case LinkExpired report:
                    await OnLinkExpiredAsync(report).ConfigureAwait(false);
                    break;
                case AttemptFinished report:
                    await OnAttemptFinishedAsync(report).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown message {message.GetType().Name}.");
            }
        }
        catch (Exception exception) when (message is Command command)
        {
            command.Reject(exception);
        }
        catch (Exception exception) when (message is Report report)
        {
            _logger.UpdateRejected(exception, report.Id);
        }
    }

    private async Task EnqueueAsync(EnqueueCommand command)
    {
        await _repository.AddAsync(command.Job, CancellationToken.None).ConfigureAwait(false);
        _jobs.Add(command.Job);

        DownloadJobSnapshot snapshot = DownloadJobSnapshot.Of(command.Job);
        _watchers.Publish(snapshot);
        command.Reply.TrySetResult(snapshot);
    }

    private async Task ControlAsync(ControlCommand command)
    {
        DownloadJob job = _jobs.Find(job => job.Id == command.Id)
            ?? throw new KeyNotFoundException($"There is no download job {command.Id}.");

        if (_running.TryGetValue(job.Id, out RunningJob? running))
        {
            if (command.Control is not (JobControl.Pause or JobControl.Cancel))
            {
                throw new InvalidOperationException($"{command.Control} isn't allowed while the download job is {job.Status}.");
            }

            // The reply completes once the download has stopped and the job has been updated.
            running.RequestStop(command.Control == JobControl.Cancel ? StopReason.Cancel : StopReason.Pause, command.Reply);
            return;
        }

        switch (command.Control)
        {
            case JobControl.Pause:
                job.Pause();
                break;
            case JobControl.Resume:
                job.Resume();
                break;
            case JobControl.Retry:
                job.Retry();
                break;
            case JobControl.Cancel:
                job.Cancel();
                break;
            default:
                throw new InvalidOperationException($"Unknown control {command.Control}.");
        }

        await SaveAsync(job).ConfigureAwait(false);
        if (command.Control == JobControl.Cancel)
        {
            await _transfer.DeletePartialFileAsync(DestinationOf(job), CancellationToken.None).ConfigureAwait(false);
        }

        command.Reply.TrySetResult();
    }

    private async Task OnTransferStartedAsync(TransferStarted report)
    {
        if (RunningJobFor(report) is { } job)
        {
            job.StartDownloading(report.ResumeFromByte, report.TotalBytes);
            _lastProgressSnapshot.Remove(job.Id);
            await SaveAsync(job).ConfigureAwait(false);
        }
    }

    private void OnTransferAdvanced(TransferAdvanced report)
    {
        if (RunningJobFor(report) is not { Status: DownloadJobStatus.Downloading } job)
        {
            return;
        }

        job.ReportProgress(report.BytesDownloaded);

        // Progress isn't saved: the partial file on disk is what a resumed download relies on.
        long now = _timeProvider.GetTimestamp();
        if (!_lastProgressSnapshot.TryGetValue(job.Id, out long last)
            || _timeProvider.GetElapsedTime(last, now) >= _options.ProgressInterval)
        {
            _lastProgressSnapshot[job.Id] = now;
            _watchers.Publish(DownloadJobSnapshot.Of(job));
        }
    }

    private async Task OnTransferVerifyingAsync(TransferVerifying report)
    {
        if (RunningJobFor(report) is { Status: DownloadJobStatus.Downloading } job)
        {
            job.StartVerifying();
            await SaveAsync(job).ConfigureAwait(false);
        }
    }

    private async Task OnLinkExpiredAsync(LinkExpired report)
    {
        // A ticket can also expire before the transfer started, while the job is still resolving.
        if (RunningJobFor(report) is { Status: DownloadJobStatus.Downloading } job)
        {
            job.StartResolving();
            await SaveAsync(job).ConfigureAwait(false);
        }
    }

    private async Task OnAttemptFinishedAsync(AttemptFinished report)
    {
        if (!_running.Remove(report.Id, out RunningJob? running))
        {
            return;
        }

        using (running)
        {
            DownloadJob job = _jobs.Find(job => job.Id == report.Id)!;
            _lastProgressSnapshot.Remove(job.Id);

            bool stopped = report.Result.End == AttemptEnd.Stopped;
            bool deletePartialFile = stopped && running.StopReason == StopReason.Cancel;
            try
            {
                Finish(job, report.Result, running.StopReason);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // The transfer broke its reporting contract, for example by completing without verifying.
                _logger.UpdateRejected(exception, job.Id);
                job.Fail(DownloadFailureReason.Unexpected);
            }

            await SaveAsync(job).ConfigureAwait(false);
            if (deletePartialFile)
            {
                await _transfer.DeletePartialFileAsync(DestinationOf(job), CancellationToken.None).ConfigureAwait(false);
            }

            running.CompleteWaiters(stopped
                ? null
                : new InvalidOperationException($"The download job was already {job.Status} when the request arrived."));
        }
    }

    private void Finish(DownloadJob job, AttemptResult result, StopReason? stopReason)
    {
        switch (result.End)
        {
            case AttemptEnd.Completed:
                if (job.Status == DownloadJobStatus.Downloading)
                {
                    job.StartVerifying();
                }

                job.Complete();
                break;

            case AttemptEnd.HandoffRequired:
                job.AwaitHandoff();
                break;

            case AttemptEnd.Failed:
                DownloadFailureReason reason = result.FailureReason ?? DownloadFailureReason.Unexpected;
                job.Fail(reason);
                _logger.JobFailed(job.Id, reason);
                break;

            case AttemptEnd.Stopped when stopReason == StopReason.Cancel:
                job.Cancel();
                break;

            case AttemptEnd.Stopped when stopReason == StopReason.Pause:
                // Verifying can't be paused directly; requeueing first keeps the user's pause.
                if (job.Status == DownloadJobStatus.Verifying)
                {
                    job.Requeue();
                }

                job.Pause();
                break;

            case AttemptEnd.Stopped:
                job.Requeue();
                break;

            default:
                throw new InvalidOperationException($"Unknown attempt result {result.End}.");
        }
    }

    private DownloadJob? RunningJobFor(Report report)
        => _running.ContainsKey(report.Id) ? _jobs.Find(job => job.Id == report.Id) : null;

    private async Task SaveAsync(DownloadJob job)
    {
        await _repository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
        _watchers.Publish(DownloadJobSnapshot.Of(job));
    }
}
