using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Links;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// Requests to change the queue, carried out in the process that runs it: what each does, and each problem as a reply.
/// The clock moves on each time it's read, so waiting for a download to start takes no time.
/// </summary>
public sealed class QueueRequestsTests
{
    private static readonly Uri Link = new("nxm://skyrimspecialedition/mods/12604/files/2?key=secret&expires=1");

    private readonly ScriptedLinks _links = new();
    private readonly ScriptedQueue _queue = new();
    private readonly RecordingLogger<QueueRequests> _logger = new();
    private readonly FakeTimeProvider _time = new() { AutoAdvanceAmount = QueueRequests.LookEvery };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DownloadsALinkIntoTheLibrary()
    {
        DownloadJobSnapshot job = Job(DownloadJobStatus.Queued);
        _links.Opened = new OpenedLink(new LinkDownload(job, AlreadyQueued: false), "/library/nexus/mod.zip");

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(new QueueReply(QueueOutcome.Done, Job: job, LibraryPath: "/library/nexus/mod.zip"), reply);
        Assert.Equal([Link], _links.Asked);
    }

    [Fact]
    public async Task SaysWhenADownloadWentOnOrTheFileIsThereAlready()
    {
        DownloadJobSnapshot waiting = Job(DownloadJobStatus.AwaitingHandoff);
        _links.Opened = new OpenedLink(new LinkDownload(waiting, AlreadyQueued: true), LibraryPath: null);
        QueueReply wentOn = await Handle(QueueRequest.Download(Link));

        _links.Opened = new OpenedLink(Download: null, "/library/nexus/mod.zip");
        QueueReply there = await Handle(QueueRequest.Download(Link));

        Assert.Equal(new QueueReply(QueueOutcome.Done, Job: waiting, AlreadyQueued: true), wentOn);
        Assert.Equal(new QueueReply(QueueOutcome.Done, LibraryPath: "/library/nexus/mod.zip", AlreadySaved: true), there);
    }

    [Fact]
    public async Task ADownloadsReplyWaitsForItToStart()
    {
        DownloadJobSnapshot queued = Job(DownloadJobStatus.Queued);
        DownloadJobSnapshot waiting = queued with { Status = DownloadJobStatus.AwaitingHandoff, HandoffPage = new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=2&nmm=1") };
        _links.Opened = new OpenedLink(new LinkDownload(queued, AlreadyQueued: false), "/library/nexus/mod.zip");
        _queue.Jobs.Add(queued with { Status = DownloadJobStatus.Resolving });
        _queue.Listed = jobs => jobs[0] = jobs[0].Status == DownloadJobStatus.Resolving ? waiting : jobs[0];

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(waiting, reply.Job);
        Assert.Equal(2, _queue.Lists);
    }

    [Fact]
    public async Task ADownloadThatDoesntStartInTimeIsRepliedAsItIs()
    {
        // Such as one behind others the queue is downloading.
        DownloadJobSnapshot queued = Job(DownloadJobStatus.Queued);
        _links.Opened = new OpenedLink(new LinkDownload(queued, AlreadyQueued: false), "/library/nexus/mod.zip");
        _queue.Jobs.Add(queued);
        DateTimeOffset asked = _time.GetUtcNow();

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(queued, reply.Job);
        Assert.InRange(_time.GetUtcNow() - asked, QueueRequests.StartWait, QueueRequests.StartWait * 2);
        Assert.InRange(_queue.Lists, 2, QueueRequests.StartWait / QueueRequests.LookEvery);
    }

    [Theory]
    [InlineData(QueueRequestKind.Pause, DownloadJobStatus.Paused)]
    [InlineData(QueueRequestKind.Resume, DownloadJobStatus.Queued)]
    [InlineData(QueueRequestKind.Retry, DownloadJobStatus.Queued)]
    [InlineData(QueueRequestKind.Cancel, DownloadJobStatus.Cancelled)]
    public async Task ChangesADownloadAndRepliesWithIt(QueueRequestKind kind, DownloadJobStatus after)
    {
        DownloadJobSnapshot job = Job(DownloadJobStatus.Downloading);
        _queue.Jobs.Add(job);

        QueueReply reply = await Handle(new QueueRequest(kind, Job: job.Id));

        Assert.Equal(QueueOutcome.Done, reply.Outcome);
        Assert.Equal(after, reply.Job?.Status);
        Assert.Equal([$"{kind} {job.Id}"], _queue.Calls);
    }

    [Fact]
    public async Task AnUnknownDownloadIsNotFound()
    {
        DownloadJobId unknown = DownloadJobId.New();

        QueueReply reply = await Handle(QueueRequest.Pause(unknown));

        Assert.Equal(new QueueReply(QueueOutcome.NotFound, $"No download has the ID {unknown}."), reply);
    }

    [Fact]
    public async Task AChangeTheStatusDoesntAllowIsNotAllowed()
    {
        DownloadJobSnapshot job = Job(DownloadJobStatus.Completed);
        _queue.Jobs.Add(job);
        _queue.Failure = new InvalidOperationException("A completed download can't be paused.");

        QueueReply reply = await Handle(QueueRequest.Pause(job.Id));

        Assert.Equal(new QueueReply(QueueOutcome.NotAllowed, "A completed download can't be paused."), reply);
    }

    [Fact]
    public async Task AQueueAnotherProcessRunsIsRefused()
    {
        _links.Failure = new DownloadQueueBusyException();

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(QueueOutcome.Refused, reply.Outcome);
        Assert.Equal(new DownloadQueueBusyException().Message, reply.Problem);
    }

    public static TheoryData<Exception> Problems() =>
    [
        new UnsupportedLinkException("No site knows this link."),
        new ArgumentException("The link isn't to a mod or a file."),
        new IOException("The file can't go there."),
        new InvalidDataException("settings.json isn't JSON."),
    ];

    public static TheoryData<Exception, string> SiteProblems() => new()
    {
        { new AuthenticationFailedException("Nexus Mods needs a linked account."), "The site needs a linked account for that, or refused the one linked. Link one in ModShelf, then try again." },
        { new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound), "The site has no such mod or file." },
        { new HttpRequestException("Gone.", null, HttpStatusCode.Gone), "The site has no such mod or file." },
        { new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests), "The site asked ModShelf to slow down. Try again in a while." },
        { new HttpRequestException("No such host is known."), "The site couldn't be asked: No such host is known." },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public async Task AProblemTheUserCanActOnIsRefusedWithItsWords(Exception problem)
    {
        _links.Failure = problem;

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(new QueueReply(QueueOutcome.Refused, problem.Message), reply);
        Assert.DoesNotContain(_logger.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SiteProblems))]
    public async Task AProblemWithTheSiteIsRefusedInWordsForTheUser(Exception problem, string words)
    {
        _links.Failure = problem;

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(new QueueReply(QueueOutcome.Refused, words), reply);
    }

    [Fact]
    public async Task AnythingElseFailsAndIsLoggedWithoutTheLink()
    {
        _links.Failure = new FormatException("Unexpected.");

        QueueReply reply = await Handle(QueueRequest.Download(Link));

        Assert.Equal(new QueueReply(QueueOutcome.Failed, "Something went wrong. ModShelf's log says what."), reply);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: A queue request failed: Download", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARequestWithoutWhatItNeedsIsRefused()
    {
        Assert.Equal(QueueOutcome.Refused, (await Handle(new QueueRequest(QueueRequestKind.Download))).Outcome);
        Assert.Equal(QueueOutcome.Refused, (await Handle(new QueueRequest(QueueRequestKind.Cancel))).Outcome);
        Assert.Equal(QueueOutcome.Refused, (await Handle(new QueueRequest((QueueRequestKind)42, Job: DownloadJobId.New()))).Outcome);
        Assert.Empty(_queue.Calls);
    }

    [Fact]
    public async Task CancellingStops()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        _links.Failure = new OperationCanceledException(cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new QueueRequests(_links, _queue, _time, _logger).HandleAsync(QueueRequest.Download(Link), cancelled.Token));
    }

    private static DownloadJobSnapshot Job(DownloadJobStatus status)
        => new(DownloadJobId.New(), TestData.File(TestData.Nexus, "2"), TestData.TargetDirectory, "mod.zip", status, 0, null, null, TestData.Now);

    private Task<QueueReply> Handle(QueueRequest request) => new QueueRequests(_links, _queue, _time, _logger).HandleAsync(request, Cancellation);

    private sealed class ScriptedLinks : IOpenedLinks
    {
        public OpenedLink? Opened { get; set; }

        public Exception? Failure { get; set; }

        public List<Uri> Asked { get; } = [];

        public Task<OpenedLink> OpenAsync(Uri link, CancellationToken cancellationToken)
        {
            Asked.Add(link);
            return Failure is { } failure ? Task.FromException<OpenedLink>(failure) : Task.FromResult(Opened!);
        }
    }

    /// <summary>
    /// A queue whose changes set the status they lead to, or throw the failure the test sets.
    /// </summary>
    private sealed class ScriptedQueue : IDownloadService
    {
        public List<DownloadJobSnapshot> Jobs { get; } = [];

        public List<string> Calls { get; } = [];

        public Exception? Failure { get; set; }

        /// <summary>
        /// What happens to the jobs each time they're listed, after the listing.
        /// </summary>
        public Action<List<DownloadJobSnapshot>>? Listed { get; set; }

        /// <summary>
        /// How many times the jobs were listed.
        /// </summary>
        public int Lists { get; private set; }

        public bool IsQueueBusyElsewhere => false;

        public Task WhenQueueRunsHereAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DownloadJobSnapshot> EnqueueAsync(ModFileRef file, string targetDirectory, string fileName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            Lists++;
            IReadOnlyList<DownloadJobSnapshot> listed = [.. Jobs];
            Listed?.Invoke(Jobs);
            return Task.FromResult(listed);
        }

        public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken) => Change(id, "Pause", DownloadJobStatus.Paused);

        public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken) => Change(id, "Resume", DownloadJobStatus.Queued);

        public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken) => Change(id, "Retry", DownloadJobStatus.Queued);

        public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken) => Change(id, "Cancel", DownloadJobStatus.Cancelled);

        public Task<int> ClearFinishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task Change(DownloadJobId id, string call, DownloadJobStatus status)
        {
            Calls.Add($"{call} {id}");
            if (Failure is { } failure)
            {
                return Task.FromException(failure);
            }

            int index = Jobs.FindIndex(job => job.Id == id);
            if (index < 0)
            {
                return Task.FromException(new KeyNotFoundException($"No job {id}."));
            }

            Jobs[index] = Jobs[index] with { Status = status };
            return Task.CompletedTask;
        }
    }
}
