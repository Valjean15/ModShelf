using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Platform.Instances;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Instances;

/// <summary>
/// Another process asks the desktop app to change the library's download queue over the queue's pipe, and gets its
/// reply: the asking side, <see cref="PipeQueueOwner"/>, and the desktop app's, <see cref="PipeQueueListener"/>.
/// </summary>
public sealed class PipeQueueTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly Uri Link = new("nxm://stardewvalley/mods/2400/files/9001?key=secret-key&expires=1");

    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<PipeQueueOwner> _ownerLogger = new();
    private readonly RecordingLogger<PipeQueueListener> _listenerLogger = new();
    private readonly ScriptedRequests _requests = new();
    private readonly List<PipeQueueListener> _listeners = [];

    // What a test holds a request on until it lets go. Disposing lets go first, so that a failed test doesn't leave a
    // request the listener waits for as it stops.
    private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _held.TrySetResult();
        foreach (PipeQueueListener listener in _listeners)
        {
            await listener.StopAsync(CancellationToken.None);
            listener.Dispose();
        }

        _folder.Dispose();
    }

    [Fact]
    public async Task ARequestReachesTheAppAndItsReplyComesBack()
    {
        QueueReply expected = new(QueueOutcome.Done, Job: Job(), LibraryPath: Path.Combine(_folder.Path, "Stardew Valley", "SMAPI.zip"));
        _requests.Reply = _ => Task.FromResult(expected);
        await ListeningAsync();

        QueueReply? reply = await Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken);

        Assert.Equal(expected, reply);
        Assert.Equal(QueueRequest.Download(Link), Assert.Single(_requests.Received));
    }

    [Fact]
    public async Task EachRequestIsHeardInTurn()
    {
        await ListeningAsync();
        PipeQueueOwner owner = Owner();
        DownloadJobId job = DownloadJobId.New();

        foreach (QueueRequest request in new[] { QueueRequest.Pause(job), QueueRequest.Resume(job), QueueRequest.Cancel(job) })
        {
            Assert.Equal(QueueOutcome.Done, (await owner.SendAsync(request, TestContext.Current.CancellationToken))?.Outcome);
        }

        Assert.Equal([QueueRequest.Pause(job), QueueRequest.Resume(job), QueueRequest.Cancel(job)], _requests.Received);
    }

    [Fact]
    public async Task WithNoAppRunningThereIsNoReplyAndItSaysSoInTime()
    {
        Stopwatch waited = Stopwatch.StartNew();

        QueueReply? reply = await Owner(patience: TimeSpan.FromMilliseconds(300)).SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.InRange(waited.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Contains(_ownerLogger.Entries, entry => entry.StartsWith("Debug: No desktop app takes queue requests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARequestThatTakesLongKeepsNoOtherWaiting()
    {
        _requests.Reply = async request =>
        {
            if (request.Kind == QueueRequestKind.Download)
            {
                await _held.Task;
            }

            return new QueueReply(QueueOutcome.Done);
        };
        await ListeningAsync();
        PipeQueueOwner owner = Owner();

        Task<QueueReply?> slow = owner.SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken);
        QueueReply? quick = await owner.SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(QueueOutcome.Done, quick?.Outcome);
        Assert.False(slow.IsCompleted);
        _held.SetResult();
        Assert.Equal(QueueOutcome.Done, (await slow.WaitAsync(Patience, TestContext.Current.CancellationToken))?.Outcome);
    }

    [Fact]
    public async Task AnAppThatDoesntReplyInTimeTimesOutAndTheNextRequestIsHeard()
    {
        _requests.Reply = async request => request.Kind == QueueRequestKind.Download ? await HeldAsync() : new QueueReply(QueueOutcome.Done);
        await ListeningAsync();

        TimeoutException timedOut = await Assert.ThrowsAsync<TimeoutException>(
            () => Owner(replyTimeout: TimeSpan.FromMilliseconds(300)).SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken));

        Assert.Equal("ModShelf's desktop app took the request, but didn't reply in time.", timedOut.Message);
        Assert.Equal(QueueOutcome.Done, (await Owner().SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken))?.Outcome);
        Assert.Contains(_ownerLogger.Entries, entry => entry.StartsWith("Warning: The desktop app took a queue request, but didn't reply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellingTheRequestCancelsItRatherThanTimingOut()
    {
        _requests.Reply = _ => HeldAsync();
        await ListeningAsync();
        using CancellationTokenSource cancelling = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancelling.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Owner().SendAsync(QueueRequest.Download(Link), cancelling.Token));
    }

    [Fact]
    public async Task AnAppThatHangsUpWithoutReplyingTimesOut()
    {
        // Such as another program on the pipe's name: it reads the request, then closes.
        await using NamedPipeServerStream rude = Impostor();
        Task hangsUp = Task.Run(
            async () =>
            {
                await rude.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                using StreamReader reader = new(rude, leaveOpen: true);
                await reader.ReadLineAsync(TestContext.Current.CancellationToken);
                rude.Disconnect();
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken));
        await hangsUp.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("not json\n")]
    [InlineData("{\"Outcome\":\"Installed\",\"Problem\":null,\"Job\":null,\"LibraryPath\":null,\"AlreadyQueued\":false,\"AlreadySaved\":false}\n")]
    public async Task AReplyItCantReadIsUnreadable(string reply)
    {
        // Such as a newer app's.
        await using NamedPipeServerStream newer = Impostor();
        Task replies = Task.Run(
            async () =>
            {
                await newer.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                using StreamReader reader = new(newer, leaveOpen: true);
                await reader.ReadLineAsync(TestContext.Current.CancellationToken);
                await newer.WriteAsync(Encoding.UTF8.GetBytes(reply), TestContext.Current.CancellationToken);
                await newer.FlushAsync(TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken));
        await replies.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("{\"Kind\":\"Archive\",\"Link\":null,\"Job\":null}\n")]
    [InlineData("not json\n")]
    [InlineData("\n")]
    public async Task ARequestTheAppDoesntKnowIsRefusedAndTheNextIsHeard(string request)
    {
        // Such as a newer CLI's, asking an app that's older.
        await ListeningAsync();

        string reply;
        await using (NamedPipeClientStream newer = await StrangerAsync())
        {
            await newer.WriteAsync(Encoding.UTF8.GetBytes(request), TestContext.Current.CancellationToken);
            await newer.FlushAsync(TestContext.Current.CancellationToken);
            using StreamReader reader = new(newer);
            reply = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken) ?? "";
        }

        QueueReply refused = QueueMessages.ReadReply(reply);
        Assert.Equal(QueueOutcome.Refused, refused.Outcome);
        Assert.Equal("The ModShelf app that's running doesn't know this request. Update it, then ask again.", refused.Problem);
        Assert.Empty(_requests.Received);
        Assert.Equal(QueueOutcome.Done, (await Owner().SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken))?.Outcome);
        Assert.Contains(_listenerLogger.Entries, entry => entry.StartsWith("Warning: A queue request couldn't be read", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20_000, true)]
    [InlineData(10, false)]
    [InlineData(0, false)]
    public async Task AMessageTooLongOrNotThereIsntReadAndTheNextRequestIsHeard(int length, bool endsTheLine)
    {
        // Too long, a line that never ends, or nothing: the app hangs up without a reply.
        await ListeningAsync(patience: TimeSpan.FromMilliseconds(500));

        await using (NamedPipeClientStream stranger = await StrangerAsync())
        {
            try
            {
                await stranger.WriteAsync(Encoding.UTF8.GetBytes(new string('x', length) + (endsTheLine ? "\n" : "")), TestContext.Current.CancellationToken);
                await stranger.FlushAsync(TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // It may hang up before it has read it all.
            }

            int answer;
            try
            {
                byte[] first = new byte[1];
                answer = await stranger.ReadAsync(first, TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                answer = 0;
            }

            Assert.Equal(0, answer);
        }

        Assert.Empty(_requests.Received);
        Assert.Equal(QueueOutcome.Done, (await Owner().SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken))?.Outcome);
        Assert.Contains(_listenerLogger.Entries, entry => entry.StartsWith("Warning: A queue request couldn't be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoppingTheAppCancelsTheRequestsItsCarryingOutAndTheAskingSideIsntLeftWaiting()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Reply = async _ =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _requests.CancellationToken);
            return new QueueReply(QueueOutcome.Done);
        };
        PipeQueueListener listener = await ListeningAsync();
        Task<QueueReply?> asking = Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        await listener.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => asking.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.True(_requests.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AFailingRequestIsLoggedAndTheNextIsHeard()
    {
        // The app's requests never throw but when cancelled; were one to, the asking side sees no reply.
        _requests.Reply = request => request.Kind == QueueRequestKind.Download
            ? throw new InvalidOperationException("Broken.")
            : Task.FromResult(new QueueReply(QueueOutcome.Done));
        await ListeningAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken));

        Assert.Equal(QueueOutcome.Done, (await Owner().SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken))?.Outcome);
        Assert.Contains(_listenerLogger.Entries, entry => entry.StartsWith("Warning: A queue request's reply couldn't be sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinksAreNeverLogged()
    {
        _requests.Reply = _ => throw new InvalidOperationException("Broken.");
        await ListeningAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => Owner().SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken));

        Assert.NotEmpty(_ownerLogger.Entries);
        Assert.NotEmpty(_listenerLogger.Entries);
        Assert.DoesNotContain(_ownerLogger.Entries.Concat(_listenerLogger.Entries), entry => entry.Contains("secret-key", StringComparison.Ordinal) || entry.Contains("nxm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherLibrarysAppIsntAsked()
    {
        using TempFolder other = new();
        await ListeningAsync();

        QueueReply? reply = await Owner(other.Path, patience: TimeSpan.FromMilliseconds(300)).SendAsync(QueueRequest.Download(Link), TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.Empty(_requests.Received);
    }

    [Fact]
    public void ThePipeIsTheLibrarysApartFromTheSingleInstances()
    {
        string? runtime = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") : null;
        PipeQueueListener listener = new(_folder.Path, _requests, Patience, _listenerLogger);

        Assert.Equal(PipeSingleInstance.PipeNameFor(_folder.Path, runtime, "-queue"), listener.PipeName);
        Assert.Equal(PipeSingleInstance.PipeNameFor(_folder.Path) + "-queue", PipeSingleInstance.PipeNameFor(_folder.Path, suffix: "-queue"));
        Assert.Matches("^ModShelf-[0-9A-F]{24}-queue$", PipeSingleInstance.PipeNameFor(_folder.Path, suffix: "-queue"));
        listener.Dispose();
    }

    [Fact]
    public async Task OnLinuxAndMacOSTheSocketCanBeInTheUsersOwnFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows' pipes aren't files.");

        // Short, as $XDG_RUNTIME_DIR is: macOS's temporary folder leaves no room for a socket's path.
        string runtime = Directory.CreateDirectory($"/tmp/ModShelf.Tests.{Guid.NewGuid():N}"[..34]).FullName;
        try
        {
            PipeQueueListener listener = new(_folder.Path, _requests, Patience, _listenerLogger, runtime);
            _listeners.Add(listener);
            await listener.StartAsync(TestContext.Current.CancellationToken);

            QueueReply? reply = await new PipeQueueOwner(_folder.Path, Patience, Patience, _ownerLogger, runtime)
                .SendAsync(QueueRequest.Pause(DownloadJobId.New()), TestContext.Current.CancellationToken);

            Assert.Equal(QueueOutcome.Done, reply?.Outcome);
            Assert.Equal(
                PipeSingleInstance.PipeNameFor(_folder.Path, suffix: "-queue"),
                Path.GetFileName(Assert.Single(Directory.EnumerateFileSystemEntries(runtime))));
        }
        finally
        {
            foreach (PipeQueueListener listener in _listeners)
            {
                await listener.StopAsync(CancellationToken.None);
                listener.Dispose();
            }

            _listeners.Clear();
            Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public void AFolderThatIsntAFullPathOrNoPatienceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new PipeQueueOwner("Library", Patience, Patience, _ownerLogger));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipeQueueOwner(_folder.Path, TimeSpan.Zero, Patience, _ownerLogger));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipeQueueOwner(_folder.Path, Patience, TimeSpan.Zero, _ownerLogger));
        Assert.Throws<ArgumentException>(() => new PipeQueueListener("Library", _requests, Patience, _listenerLogger));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipeQueueListener(_folder.Path, _requests, TimeSpan.Zero, _listenerLogger));
    }

    /// <summary>
    /// A reply once the test lets go of it, whatever the request's token says, as a request that takes long does.
    /// </summary>
    private async Task<QueueReply> HeldAsync()
    {
        await _held.Task;
        return new QueueReply(QueueOutcome.Done);
    }

    private static DownloadJobSnapshot Job()
        => new(
            DownloadJobId.New(),
            new ModFileRef(new ModRef(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400"), "9001"),
            Path.Combine(Path.GetTempPath(), "Stardew Valley"),
            "SMAPI.zip",
            DownloadJobStatus.Downloading,
            1_024,
            4_096,
            null,
            new DateTimeOffset(2026, 11, 1, 10, 30, 15, TimeSpan.Zero));

    private PipeQueueOwner Owner(string? folder = null, TimeSpan? patience = null, TimeSpan? replyTimeout = null)
        => new(folder ?? _folder.Path, patience ?? Patience, replyTimeout ?? Patience, _ownerLogger, runtimeFolder: null);

    private async Task<PipeQueueListener> ListeningAsync(TimeSpan? patience = null)
    {
        PipeQueueListener listener = new(_folder.Path, _requests, patience ?? Patience, _listenerLogger, runtimeFolder: null);
        _listeners.Add(listener);
        await listener.StartAsync(TestContext.Current.CancellationToken);
        return listener;
    }

    /// <summary>
    /// Another program on the queue's pipe's name, in place of the app.
    /// </summary>
    private NamedPipeServerStream Impostor()
        => new(
            PipeSingleInstance.PipeNameFor(_folder.Path, suffix: "-queue"),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>
    /// Another program connected to the app's queue pipe; it waits for the app to listen.
    /// </summary>
    private async Task<NamedPipeClientStream> StrangerAsync()
    {
        NamedPipeClientStream stranger = new(
            ".",
            PipeSingleInstance.PipeNameFor(_folder.Path, suffix: "-queue"),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await stranger.ConnectAsync(Patience, TestContext.Current.CancellationToken);
            return stranger;
        }
        catch
        {
            await stranger.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The app's queue, answering as a test says, done by default.
    /// </summary>
    private sealed class ScriptedRequests : IQueueRequests
    {
        private readonly ConcurrentQueue<QueueRequest> _received = new();

        public Func<QueueRequest, Task<QueueReply>> Reply { get; set; } = _ => Task.FromResult(new QueueReply(QueueOutcome.Done));

        public IReadOnlyCollection<QueueRequest> Received => _received;

        /// <summary>
        /// The last request's token.
        /// </summary>
        public CancellationToken CancellationToken { get; private set; }

        public Task<QueueReply> HandleAsync(QueueRequest request, CancellationToken cancellationToken)
        {
            _received.Enqueue(request);
            CancellationToken = cancellationToken;
            return Reply(request);
        }
    }
}
