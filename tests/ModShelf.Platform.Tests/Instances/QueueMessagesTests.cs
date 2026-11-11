using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Platform.Instances;

namespace ModShelf.Platform.Tests.Instances;

/// <summary>
/// Queue requests and replies as the queue's pipe carries them: each comes back as it went, and a line that isn't one
/// is refused, never taken for something else.
/// </summary>
public sealed class QueueMessagesTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=9001");

    public static TheoryData<QueueRequest> Requests =>
    [
        QueueRequest.Download(Link),
        QueueRequest.Download(new Uri("nxm://stardewvalley/mods/2400/files/9001?key=secret-key&expires=1")),
        QueueRequest.Pause(DownloadJobId.New()),
        QueueRequest.Resume(DownloadJobId.New()),
        QueueRequest.Retry(DownloadJobId.New()),
        QueueRequest.Cancel(DownloadJobId.New()),
        QueueRequest.TakeFile(DownloadJobId.New(), Path.Combine(Path.GetTempPath(), "Downloads", "SMAPI 4.0 (1).zip")),
    ];

    public static TheoryData<QueueReply> Replies =>
    [
        new QueueReply(QueueOutcome.Done, Job: Job(), LibraryPath: Path.Combine(Path.GetTempPath(), "Stardew Valley", "SMAPI.zip")),
        new QueueReply(QueueOutcome.Done, LibraryPath: Path.Combine(Path.GetTempPath(), "SMAPI.zip"), AlreadySaved: true),
        new QueueReply(QueueOutcome.Done, Job: Job() with { Status = DownloadJobStatus.Queued, TotalBytes = null, HandoffPage = null }, AlreadyQueued: true),
        new QueueReply(QueueOutcome.Done, Job: Job() with { Status = DownloadJobStatus.Failed, FailureReason = DownloadFailureReason.Network }),
        new QueueReply(QueueOutcome.NotFound, "No download has that ID."),
        new QueueReply(QueueOutcome.NotAllowed, "A finished download can't be paused."),
        new QueueReply(QueueOutcome.Refused, "Another ModShelf runs this library's downloads."),
        new QueueReply(QueueOutcome.Failed, "Something went wrong. ModShelf's log says what."),
    ];

    [Theory]
    [MemberData(nameof(Requests))]
    public void ARequestComesBackAsItWent(QueueRequest request)
    {
        Assert.Equal(request, QueueMessages.ReadRequest(QueueMessages.Write(request)));
    }

    [Theory]
    [MemberData(nameof(Replies))]
    public void AReplyComesBackAsItWent(QueueReply reply)
    {
        Assert.Equal(reply, QueueMessages.ReadReply(QueueMessages.Write(reply)));
    }

    [Fact]
    public void EnumsGoAsTheirNamesOnOneLine()
    {
        string request = QueueMessages.Write(QueueRequest.Pause(DownloadJobId.New()));
        string reply = QueueMessages.Write(new QueueReply(QueueOutcome.Done, Job: Job()));

        Assert.Contains("\"Kind\":\"Pause\"", request, StringComparison.Ordinal);
        Assert.Contains("\"Outcome\":\"Done\"", reply, StringComparison.Ordinal);
        Assert.Contains("\"Status\":\"Downloading\"", reply, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', request);
        Assert.DoesNotContain('\n', reply);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Kind\":\"Install\",\"Link\":null,\"Job\":null}")]
    [InlineData("{\"Kind\":\"0\",\"Link\":null,\"Job\":null}")]
    [InlineData("{\"Kind\":0,\"Link\":null,\"Job\":null}")]
    [InlineData("{\"Kind\":\"download\",\"Link\":null,\"Job\":null}")]
    [InlineData("{\"Kind\":\"Download\",\"Link\":\"mods/2400\",\"Job\":null}")]
    [InlineData("{\"Kind\":\"Pause\",\"Link\":null,\"Job\":\"not a job\"}")]
    [InlineData("{\"Kind\":\"Pause\",\"Link\":null,\"Job\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void ALineThatIsntARequestIsRefused(string line)
    {
        Assert.Throws<InvalidDataException>(() => QueueMessages.ReadRequest(line));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Outcome\":\"Installed\",\"Problem\":null,\"Job\":null,\"LibraryPath\":null,\"AlreadyQueued\":false,\"AlreadySaved\":false}")]
    [InlineData("{\"Outcome\":\"4\",\"Problem\":null,\"Job\":null,\"LibraryPath\":null,\"AlreadyQueued\":false,\"AlreadySaved\":false}")]
    public void ALineThatIsntAReplyIsRefused(string line)
    {
        Assert.Throws<InvalidDataException>(() => QueueMessages.ReadReply(line));
    }

    [Theory]
    [InlineData("\"Status\":\"Downloading\"", "\"Status\":\"Unpacking\"")]
    [InlineData("\"Source\":\"nexus\"", "\"Source\":\"Nexus Mods\"")]
    [InlineData("\"FailureReason\":null", "\"FailureReason\":\"Gremlins\"")]
    [InlineData("\"HandoffPage\":\"https://www.nexusmods.com/stardewvalley/mods/2400?tab=files\\u0026file_id=9001\"", "\"HandoffPage\":\"mods/2400\"")]
    public void AReplyWhoseDownloadIsntOneIsRefused(string part, string unknown)
    {
        string line = QueueMessages.Write(new QueueReply(QueueOutcome.Done, Job: Job()));
        Assert.Contains(part, line, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => QueueMessages.ReadReply(line.Replace(part, unknown, StringComparison.Ordinal)));
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
            new DateTimeOffset(2026, 11, 1, 10, 30, 15, 123, TimeSpan.FromHours(1)).AddTicks(4_567),
            Link);
}
