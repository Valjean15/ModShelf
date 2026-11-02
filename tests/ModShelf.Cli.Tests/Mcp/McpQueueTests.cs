using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// The queue as the MCP server changes it: the desktop app's when it runs, otherwise its own, run in a host of its own
/// until no download is under way, and the finished ones are in the library.
/// </summary>
public sealed class McpQueueTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly Uri Link = new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=9001");

    private readonly ScriptedQueueOwner _desktopApp = new();
    private readonly ScriptedDownloads _downloads = new();
    private readonly RecordingQueueRequests _requests = new();
    private readonly HeldRecording _library = new();
    private readonly List<RunnerHost> _runners = [];
    private readonly McpQueue _queue;

    public McpQueueTests()
    {
        _queue = new McpQueue(_desktopApp, BuildRunner, TimeSpan.FromMilliseconds(50), TimeProvider.System, NullLogger<McpQueue>.Instance);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        foreach (RunnerHost runner in _runners)
        {
            runner.Dispose();
        }
    }

    [Fact]
    public async Task ARunningDesktopAppIsAsked()
    {
        _desktopApp.Reply = new QueueReply(QueueOutcome.NotAllowed, "Not now.");

        (QueueReply reply, bool ranHere) = await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);

        Assert.Equal((new QueueReply(QueueOutcome.NotAllowed, "Not now."), false), (reply, ranHere));
        Assert.Empty(_runners);
        Assert.False(_queue.RunsHere);
        Assert.Null(await _queue.ListHereAsync(Cancellation));
    }

    [Fact]
    public async Task WithNoDesktopAppTheQueueRunsHereUntilNothingIsUnderWay()
    {
        _desktopApp.Reply = null;
        _downloads.Jobs = [Job(DownloadJobStatus.Downloading), Job(DownloadJobStatus.Completed)];

        (QueueReply reply, bool ranHere) = await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);

        Assert.Equal((QueueOutcome.Done, true), (reply.Outcome, ranHere));
        Assert.Equal([QueueRequest.Download(Link)], _requests.Requests);
        Assert.True(_queue.RunsHere);
        Assert.Equal(_downloads.Jobs, await _queue.ListHereAsync(Cancellation));
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        Assert.True(_queue.RunsHere);

        // A download waiting for the user isn't under way.
        _downloads.Jobs = [Job(DownloadJobStatus.AwaitingHandoff), Job(DownloadJobStatus.Paused), Job(DownloadJobStatus.Failed)];

        await WaitUntilAsync(() => !_queue.RunsHere);
        RunnerHost runner = Assert.Single(_runners);
        await runner.Stopped.WaitAsync(Patience, Cancellation);
        Assert.True(runner.Disposed);
    }

    [Fact]
    public async Task ItStopsOnlyOnceTheFinishedDownloadsAreInTheLibrary()
    {
        // Stopping the host while a download that just finished is recorded would stop the recording, leaving the file
        // out of the library until the downloads run again.
        _desktopApp.Reply = null;
        _downloads.Jobs = [Job(DownloadJobStatus.Completed)];
        _library.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);
        RunnerHost runner = Assert.Single(_runners);

        await WaitUntilAsync(() => _library.MissedRecorded > 0);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        bool stoppedWhileRecording = runner.Stopped.IsCompleted;
        _library.Hold.SetResult();

        await WaitUntilAsync(() => !_queue.RunsHere);
        await runner.Stopped.WaitAsync(Patience, Cancellation);
        Assert.False(stoppedWhileRecording);
        Assert.Equal(1, _library.MissedRecorded);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Queued)]
    [InlineData(DownloadJobStatus.Resolving)]
    [InlineData(DownloadJobStatus.Verifying)]
    public async Task ADownloadAboutToDownloadOrCheckedIsUnderWay(DownloadJobStatus status)
    {
        _desktopApp.Reply = null;
        _downloads.Jobs = [Job(status)];

        await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);

        Assert.True(_queue.RunsHere);
    }

    [Fact]
    public async Task OnceItStoppedTheNextRequestAsksTheDesktopAppAgain()
    {
        _desktopApp.Reply = null;
        await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);
        await WaitUntilAsync(() => !_queue.RunsHere);

        _desktopApp.Reply = new QueueReply(QueueOutcome.Done);
        (_, bool ranHere) = await _queue.SendAsync(QueueRequest.Pause(DownloadJobId.New()), Cancellation);

        Assert.False(ranHere);
        Assert.Equal(2, _desktopApp.Requests.Count);
        Assert.Single(_runners);
    }

    [Fact]
    public async Task WhileItRunsHereTheDesktopAppIsntAsked()
    {
        _desktopApp.Reply = null;
        _downloads.Jobs = [Job(DownloadJobStatus.Downloading)];
        await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);

        (_, bool ranHere) = await _queue.SendAsync(QueueRequest.Pause(DownloadJobId.New()), Cancellation);

        Assert.True(ranHere);
        Assert.Single(_desktopApp.Requests);
        Assert.Equal(2, _requests.Requests.Count);
    }

    [Fact]
    public async Task AQueueAnotherProcessRunsIsRefusedAndNotRunHere()
    {
        // Such as the CLI's download command, or a desktop app from before the queue's pipe.
        _desktopApp.Reply = null;
        _downloads.IsQueueBusyElsewhere = true;

        (QueueReply reply, bool ranHere) = await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);

        Assert.Equal((new QueueReply(QueueOutcome.Refused, McpProblems.QueueBusyElsewhere), false), (reply, ranHere));
        Assert.Empty(_requests.Requests);
        Assert.False(_queue.RunsHere);
        Assert.True(Assert.Single(_runners).Disposed);
    }

    [Fact]
    public async Task ADesktopAppThatDoesntReplyIsTheCallersToSay()
    {
        _desktopApp.Fails = new TimeoutException("No reply.");

        await Assert.ThrowsAsync<TimeoutException>(() => _queue.SendAsync(QueueRequest.Download(Link), Cancellation));

        Assert.Empty(_runners);
    }

    [Fact]
    public async Task DisposingStopsTheQueueHereAndTakesNoMoreRequests()
    {
        _desktopApp.Reply = null;
        _downloads.Jobs = [Job(DownloadJobStatus.Downloading)];
        await _queue.SendAsync(QueueRequest.Download(Link), Cancellation);

        await _queue.DisposeAsync();

        RunnerHost runner = Assert.Single(_runners);
        Assert.True(runner.Stopped.IsCompleted);
        Assert.True(runner.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _queue.SendAsync(QueueRequest.Download(Link), Cancellation));
        await _queue.DisposeAsync();
    }

    private static DownloadJobSnapshot Job(DownloadJobStatus status)
        => new(
            DownloadJobId.New(),
            new ModFileRef(new ModRef(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400"), "9001"),
            Path.GetTempPath(),
            "SMAPI.zip",
            status,
            0,
            null,
            null,
            DateTimeOffset.UnixEpoch);

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        patience.CancelAfter(Patience);
        while (!done())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), patience.Token);
        }
    }

    private RunnerHost BuildRunner()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDownloadService>(_downloads);
        builder.Services.AddSingleton<IQueueRequests>(_requests);
        builder.Services.AddSingleton<ILibraryDownloads>(_library);
        RunnerHost runner = new(builder.Build());
        _runners.Add(runner);
        return runner;
    }

    /// <summary>
    /// The library's downloads in the host the queue runs in here: recording the missed ones waits for <see cref="Hold"/>,
    /// as for a download being recorded.
    /// </summary>
    private sealed class HeldRecording : ILibraryDownloads
    {
        private int _missedRecorded;

        public TaskCompletionSource? Hold { get; set; }

        public int MissedRecorded => Volatile.Read(ref _missedRecorded);

        public Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LibraryRecord?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task RecordMissedAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _missedRecorded);
            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// The host the queue runs in here, telling when it stops and is disposed.
    /// </summary>
    private sealed class RunnerHost(IHost inner) : IHost
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IServiceProvider Services => inner.Services;

        public Task Stopped => _stopped.Task;

        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await inner.StopAsync(cancellationToken);
            _stopped.TrySetResult();
        }

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }
    }
}
