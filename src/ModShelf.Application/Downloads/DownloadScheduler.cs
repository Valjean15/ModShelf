using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Common;
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
/// A job waiting for a handoff resolves again as soon as the user hands over a link for its file. Handoff links
/// and the pages to start them on live in memory only: links can carry a key, and pages come from the source.
/// </para>
/// <para>
/// Register one instance as both <see cref="IDownloadService"/> and a hosted service. On start it requeues
/// jobs that were interrupted mid-download, and asks the sources again about jobs that were waiting for a
/// handoff, since their pages are gone; on stop it requeues the running ones, so they continue next time.
/// </para>
/// <para>
/// A job that can't be saved, such as while the database is locked or the disk is full, goes on all the same: the
/// failure is logged, watchers still hear of the change, and the job is saved again as soon as a save works. Stored jobs
/// that can't be read at start are read again every few seconds, with requests waiting meanwhile.
/// </para>
/// <para>
/// Only the process holding the <see cref="IDownloadQueueLock"/> runs the queue. One that starts while another holds it
/// lists the jobs as saved and refuses everything else (<see cref="DownloadQueueBusyException"/>), trying the lock again
/// every <see cref="DownloadSchedulerOptions.QueueLockRetryInterval"/>. Once it holds it, it loads the jobs and runs
/// them as at any start.
/// </para>
/// </remarks>
internal sealed partial class DownloadScheduler : BackgroundService, IDownloadService
{
    // How long the scheduler waits before reading the stored jobs again when the database couldn't be read at start.
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IDownloadJobRepository _repository;
    private readonly ISourceRegistry _sources;
    private readonly IFileTransfer _transfer;
    private readonly IDownloadQueueLock _queueLock;
    private readonly ILibraryStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly DownloadSchedulerOptions _options;
    private readonly ILogger<DownloadScheduler> _logger;

    private readonly Channel<Message> _inbox =
        Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });

    private readonly DownloadWatchers _watchers = new();

    private readonly TaskCompletionSource _runsHere = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _busyElsewhere;

    // Owned by the loop: only read or changed while it handles a message.
    private readonly List<DownloadJob> _jobs = [];
    private readonly Dictionary<DownloadJobId, RunningJob> _running = [];
    private readonly Dictionary<DownloadJobId, long> _lastProgressSnapshot = [];
    private readonly Dictionary<ModFileRef, Uri> _handoffs = [];
    private readonly Dictionary<DownloadJobId, Uri> _handoffPages = [];

    // Jobs whose last save failed, such as while the database was locked: saved again once a save works.
    private readonly HashSet<DownloadJobId> _unsaved = [];
    private bool _stopping;

    public DownloadScheduler(
        IDownloadJobRepository repository,
        ISourceRegistry sources,
        IFileTransfer transfer,
        IDownloadQueueLock queueLock,
        ILibraryStorage storage,
        TimeProvider timeProvider,
        DownloadSchedulerOptions options,
        ILogger<DownloadScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(queueLock);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentDownloads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentDownloadsPerSource);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxLinkRefreshes);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ProgressInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.QueueLockRetryInterval, TimeSpan.Zero);

        _repository = repository;
        _sources = sources;
        _transfer = transfer;
        _queueLock = queueLock;
        _storage = storage;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public bool IsQueueBusyElsewhere => _busyElsewhere;

    public Task WhenQueueRunsHereAsync(CancellationToken cancellationToken) => _runsHere.Task.WaitAsync(cancellationToken);

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

    public async Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(handoff);
        if (!handoff.IsAbsoluteUri)
        {
            throw new ArgumentException("The handoff link must be absolute.", nameof(handoff));
        }

        TaskCompletionSource reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new HandOffCommand(file, handoff, reply));
        await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task<int> ClearFinishedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<int> reply = NewReply<int>();
        Post(new ClearFinishedCommand(reply));
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken)
        => _watchers.Watch(cancellationToken);

    /// <summary>
    /// Takes the queue lock before anything else starts, so <see cref="IsQueueBusyElsewhere"/> is known once the host
    /// has started.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_queueLock.TryAcquire())
        {
            _runsHere.TrySetResult();
        }
        else
        {
            _busyElsewhere = true;
            LogQueueBusyElsewhere(_logger);
        }

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // BackgroundService skips ExecuteAsync entirely when the host stops before it got to run. Nothing
        // else would then answer pending requests, so close the inbox here unless the loop is still busy.
        if (ExecuteTask is null || ExecuteTask.IsCompleted)
        {
            CloseInbox();
            _queueLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_busyElsewhere && !await WaitForQueueAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }

            if (!await LoadJobsAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }

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
            _queueLock.Release();
        }
    }

    private static TaskCompletionSource<T> NewReply<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string DestinationOf(DownloadJob job)
        => Path.Combine(job.TargetDirectory, job.FileName);

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

    /// <summary>
    /// Waits for the queue lock while another process holds it, trying again every so often. Meanwhile listing reads
    /// the jobs as saved, and anything else is refused.
    /// </summary>
    /// <returns>Whether this process holds the lock now; false when the host stopped first.</returns>
    private async Task<bool> WaitForQueueAsync(CancellationToken stoppingToken)
    {
        Task<bool> readable = _inbox.Reader.WaitToReadAsync(stoppingToken).AsTask();
        Task retry = Task.Delay(_options.QueueLockRetryInterval, _timeProvider, stoppingToken);
        while (true)
        {
            await Task.WhenAny(readable, retry).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested)
            {
                return false;
            }

            if (readable.IsCompleted)
            {
                while (_inbox.Reader.TryRead(out Message? message))
                {
                    await HandleWhileBusyElsewhereAsync(message).ConfigureAwait(false);
                }

                readable = _inbox.Reader.WaitToReadAsync(stoppingToken).AsTask();
            }

            if (retry.IsCompleted)
            {
                if (_queueLock.TryAcquire())
                {
                    _busyElsewhere = false;
                    _runsHere.TrySetResult();
                    LogQueueRunsHere(_logger);
                    return true;
                }

                retry = Task.Delay(_options.QueueLockRetryInterval, _timeProvider, stoppingToken);
            }
        }
    }

    private async Task HandleWhileBusyElsewhereAsync(Message message)
    {
        if (message is ListCommand list)
        {
            try
            {
                IReadOnlyList<DownloadJob> saved = await _repository.ListAsync(CancellationToken.None).ConfigureAwait(false);
                list.Reply.TrySetResult([.. saved.Select(Snapshot)]);
            }
            catch (Exception exception)
            {
                list.Reject(exception);
            }
        }
        else
        {
            (message as Command)?.Reject(new DownloadQueueBusyException());
        }
    }

    /// <summary>
    /// Reads the stored jobs, trying again until the database can be read, and requeues those interrupted.
    /// </summary>
    /// <returns>False when stopped first.</returns>
    private async Task<bool> LoadJobsAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                _jobs.AddRange(await _repository.ListAsync(stoppingToken).ConfigureAwait(false));
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                // Such as a database another program holds past SQLite's wait. Requests wait in the inbox meanwhile.
                LogCantLoadJobs(_logger, exception, LoadRetryDelay);
                try
                {
                    await Task.Delay(LoadRetryDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        int requeued = 0;
        foreach (DownloadJob job in _jobs)
        {
            if (job.Status is DownloadJobStatus.Resolving or DownloadJobStatus.Downloading or DownloadJobStatus.Verifying)
            {
                if (IsInPlace(job))
                {
                    // Its file was moved into place, but the app was ended, or the database held, before that was saved.
                    if (job.Status == DownloadJobStatus.Downloading)
                    {
                        job.StartVerifying();
                    }

                    job.Complete();
                    LogCompletedOnStart(_logger, job.Id.Value);
                }
                else
                {
                    job.Requeue();
                    requeued++;
                }

                await SaveAsync(job).ConfigureAwait(false);
            }
        }

        if (requeued > 0)
        {
            LogRequeuedInterruptedJobs(_logger, requeued);
        }

        return true;
    }

    /// <summary>
    /// Whether an interrupted job's file is in place already: one that was being verified, whose file is there; or one
    /// that was downloading, whose file is there whole. The transfer never starts with a file in the way, and moves the
    /// verified file into place in one step, so such a file is the job's.
    /// </summary>
    private bool IsInPlace(DownloadJob job)
    {
        long? size;
        try
        {
            size = _storage.FileSize(DestinationOf(job));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return job.Status switch
        {
            DownloadJobStatus.Verifying => size is { } verified && (job.TotalBytes is null || verified == job.TotalBytes),
            DownloadJobStatus.Downloading => size is { } whole && whole == job.TotalBytes,
            _ => false,
        };
    }

    private async Task StartQueuedJobsAsync()
    {
        if (_stopping)
        {
            return;
        }

        // Waiting jobs go first: a handoff link the user just gave may expire soon.
        List<DownloadJob> ready =
        [
            .. _jobs.Where(IsReadyToResolveAgain),
            .. _jobs.Where(job => job.Status == DownloadJobStatus.Queued),
        ];
        foreach (DownloadJob job in ready)
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
                LogNoResolver(_logger, job.Id, source);
                job.Fail(DownloadFailureReason.LinkUnavailable);
                await SaveAsync(job).ConfigureAwait(false);
                continue;
            }

            await SaveAsync(job).ConfigureAwait(false);
            Start(job, resolver);
        }
    }

    /// <summary>
    /// Whether a job waiting for a handoff should ask its source again: the user has handed over a link for its
    /// file, or the scheduler doesn't know the page to show, because the app restarted since the source named it.
    /// </summary>
    private bool IsReadyToResolveAgain(DownloadJob job)
        => job.Status == DownloadJobStatus.AwaitingHandoff
            && (_handoffs.ContainsKey(job.File) || !_handoffPages.ContainsKey(job.Id));

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

        await SaveUnsavedAsync().ConfigureAwait(false);
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
                case HandOffCommand command:
                    _handoffs[command.File] = command.Link;
                    command.Reply.TrySetResult();
                    break;
                case ListCommand command:
                    command.Reply.TrySetResult([.. _jobs.Select(Snapshot)]);
                    break;
                case ControlCommand command:
                    await ControlAsync(command).ConfigureAwait(false);
                    break;
                case ClearFinishedCommand command:
                    await ClearFinishedAsync(command).ConfigureAwait(false);
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
            LogUpdateRejected(_logger, exception, report.Id);
        }
    }

    private async Task EnqueueAsync(EnqueueCommand command)
    {
        // Two jobs for one place would share a partial file: the second would carry on from the first one's bytes.
        string destination = DestinationOf(command.Job);
        if (_jobs.Exists(job => !job.IsFinished && FilePaths.Same(DestinationOf(job), destination)))
        {
            throw new ArgumentException(
                $"Another download is saving a file to {destination} already. Choose another name or folder, or wait until it's done.");
        }

        await _repository.AddAsync(command.Job, CancellationToken.None).ConfigureAwait(false);
        _jobs.Add(command.Job);

        DownloadJobSnapshot snapshot = Snapshot(command.Job);
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
            await DeletePartialFileAsync(job).ConfigureAwait(false);
        }

        command.Reply.TrySetResult();
    }

    /// <summary>
    /// Deletes a cancelled job's partial file, unless another unfinished job saves to the same place: two such jobs,
    /// stored before they were refused, share it.
    /// </summary>
    private Task DeletePartialFileAsync(DownloadJob cancelled)
    {
        string destination = DestinationOf(cancelled);
        return _jobs.Exists(job => job != cancelled && !job.IsFinished && FilePaths.Same(DestinationOf(job), destination))
            ? Task.CompletedTask
            : _transfer.DeletePartialFileAsync(destination, CancellationToken.None);
    }

    private async Task ClearFinishedAsync(ClearFinishedCommand command)
    {
        // A finished job never runs, so nothing else holds it.
        DownloadJob[] finished = [.. _jobs.Where(job => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled)];
        if (finished.Length > 0)
        {
            HashSet<DownloadJobId> ids = [.. finished.Select(job => job.Id)];
            await _repository.DeleteAsync(ids, CancellationToken.None).ConfigureAwait(false);
            _jobs.RemoveAll(job => ids.Contains(job.Id));
            foreach (DownloadJobId id in ids)
            {
                _lastProgressSnapshot.Remove(id);
                _handoffPages.Remove(id);
            }

            LogClearedFinished(_logger, finished.Length);
        }

        command.Reply.TrySetResult(finished.Length);
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
            _watchers.Publish(Snapshot(job));
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

            // A job left waiting for a handoff can still be paused or cancelled, so a request that came in while the
            // source was asked applies, as it does to a stopped download.
            bool stopped = report.Result.End == AttemptEnd.Stopped
                || (report.Result.End == AttemptEnd.HandoffRequired && running.StopReason is StopReason.Pause or StopReason.Cancel);
            bool deletePartialFile = stopped && running.StopReason == StopReason.Cancel;
            try
            {
                Finish(job, report.Result, running);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // The transfer broke its reporting contract, for example by completing without verifying.
                LogUpdateRejected(_logger, exception, job.Id);
                job.Fail(DownloadFailureReason.Unexpected);
            }

            try
            {
                await SaveAsync(job).ConfigureAwait(false);
                if (deletePartialFile)
                {
                    await DeletePartialFileAsync(job).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                // Whoever asked for the stop hears what went wrong, rather than waiting for good.
                running.CompleteWaiters(exception);
                throw;
            }

            running.CompleteWaiters(stopped
                ? null
                : new InvalidOperationException($"The download job was already {job.Status} when the request arrived."));
        }
    }

    private void Finish(DownloadJob job, AttemptResult result, RunningJob running)
    {
        StopReason? stopReason = running.StopReason;
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
                Uri page = result.HandoffPage ?? throw new InvalidOperationException("The attempt asked for a handoff without a page.");
                job.AwaitHandoff();
                if (stopReason == StopReason.Cancel)
                {
                    job.Cancel();
                }
                else if (stopReason == StopReason.Pause)
                {
                    job.Pause();
                }
                else if (!_handoffs.TryGetValue(job.File, out Uri? latest) || ReferenceEquals(latest, running.Handoff))
                {
                    // The source refused the link the attempt had, if it had one, so the job waits for the user to
                    // start the download again.
                    _handoffs.Remove(job.File);
                    _handoffPages[job.Id] = page;
                }

                // Otherwise a newer link came in while the source was asked: the job asks again with it straight
                // away, without showing the page.
                break;

            case AttemptEnd.Failed:
                DownloadFailureReason reason = result.FailureReason ?? DownloadFailureReason.Unexpected;
                job.Fail(reason);
                LogJobFailed(_logger, job.Id, reason);
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

    private DownloadJobSnapshot Snapshot(DownloadJob job)
        => DownloadJobSnapshot.Of(job, _handoffPages.GetValueOrDefault(job.Id));

    /// <summary>
    /// Saves and publishes a job that changed, and lets go of the handoff state it no longer needs.
    /// </summary>
    /// <summary>
    /// Saves the job and tells the watchers. A save that fails, such as while the database is locked or the disk is full,
    /// is logged rather than thrown, since the download goes on either way: the job is saved again once a save works.
    /// </summary>
    private async Task SaveAsync(DownloadJob job)
    {
        ForgetHandoffsNoLongerNeeded(job);
        if (await TrySaveAsync(job).ConfigureAwait(false))
        {
            await SaveUnsavedAsync().ConfigureAwait(false);
        }

        _watchers.Publish(Snapshot(job));
    }

    private async Task<bool> TrySaveAsync(DownloadJob job)
    {
        try
        {
            await _repository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
            _unsaved.Remove(job.Id);
            return true;
        }
        catch (Exception exception)
        {
            LogSaveFailed(_logger, exception, job.Id);
            _unsaved.Add(job.Id);
            return false;
        }
    }

    /// <summary>
    /// Saves the jobs whose saves failed, as they are now, stopping at the first that fails again. A job cleared from the
    /// queue meanwhile is forgotten.
    /// </summary>
    private async Task SaveUnsavedAsync()
    {
        foreach (DownloadJobId id in _unsaved.ToList())
        {
            if (_jobs.Find(job => job.Id == id) is not { } job)
            {
                _unsaved.Remove(id);
            }
            else if (!await TrySaveAsync(job).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Drops the job's page once it stops waiting, and the handoff link for its file once no unfinished job for
    /// the file is left. A failed job keeps the link, so a retry can use it.
    /// </summary>
    private void ForgetHandoffsNoLongerNeeded(DownloadJob job)
    {
        if (job.Status != DownloadJobStatus.AwaitingHandoff)
        {
            _handoffPages.Remove(job.Id);
        }

        if (!_jobs.Any(other => other.File == job.File && !IsFinished(other)))
        {
            _handoffs.Remove(job.File);
        }

        static bool IsFinished(DownloadJob job)
            => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled;
    }
}
