using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Downloads;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Downloads;

/// <summary>
/// The transfer itself, against a scripted server with no retry pipeline in between.
/// </summary>
public sealed class HttpFileTransferTests : IDisposable
{
    private const string UrlSecret = "signature-0f9e8d7c";

    private static readonly byte[] File1000 = Responses.SampleFile(1000);

    private readonly TempFolder _folder = new();
    private readonly ScriptedHttpHandler _server = new();
    private readonly RecordingProgress _progress = new();
    private readonly RecordingLogger<HttpFileTransfer> _logger = new();

    private string Destination => Path.Combine(_folder.Path, "SkyUI_5_2_SE.7z");

    private string PartialFile => Destination + ".part";

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task DownloadsTheWholeFileAndMovesItIntoPlace()
    {
        _server.Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.False(File.Exists(PartialFile));
        Assert.Null(Assert.Single(_server.Requests).Range);
        string[] expected = ["Started 0/1000", "Advanced 1000", "Verifying"];
        Assert.Equal(expected, _progress.Events);
    }

    [Fact]
    public async Task ResumesThePartialFileWithARangeRequest()
    {
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Rest(File1000, from: 400));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Equal("bytes=400-", Assert.Single(_server.Requests).Range);
        Assert.Equal("Started 400/1000", _progress.Events[0]);
    }

    [Fact]
    public async Task StartsOverWhenTheServerIgnoresTheRange()
    {
        File.WriteAllBytes(PartialFile, Enumerable.Repeat((byte)0xFF, 400).ToArray());
        _server.Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Equal("Started 0/1000", _progress.Events[0]);
    }

    [Fact]
    public async Task StartsOverWhenTheServerSendsADifferentRange()
    {
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Rest(File1000, from: 0)).Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        string?[] ranges = [.. _server.Requests.Select(request => request.Range)];
        string?[] expected = ["bytes=400-", null];
        Assert.Equal(expected, ranges);
    }

    [Fact]
    public async Task FinishesWhenThePartialFileIsAlreadyComplete()
    {
        File.WriteAllBytes(PartialFile, File1000);
        _server.Respond(Responses.RangeNotSatisfiable(1000));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task StartsOverWhenThePartialFileIsLongerThanTheServersFile()
    {
        File.WriteAllBytes(PartialFile, Responses.SampleFile(1200));
        _server.Respond(Responses.RangeNotSatisfiable(1000)).Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task ResumingAsksForTheRestOnlyIfTheFileIsStillTheOneStarted()
    {
        _server.Respond(Responses.Whole(File1000, stopAfter: 400, etag: "\"v1\""));
        Assert.Equal(TransferOutcome.NetworkError, await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 }));
        _server.Respond(Responses.Rest(File1000, from: 400, etag: "\"v1\""));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Equal("bytes=400-", _server.Requests[^1].Range);
        Assert.Equal("\"v1\"", _server.Requests[^1].Headers!["If-Range"]);
    }

    [Fact]
    public async Task ResumingAsksWithTheFilesDateWithoutAStrongTag()
    {
        // A weak tag can't stand for the bytes, so the date when the file last changed does.
        DateTimeOffset changed = new(2026, 10, 21, 7, 28, 0, TimeSpan.Zero);
        _server.Respond(Responses.Whole(File1000, stopAfter: 400, etag: "W/\"v1\"", lastModified: changed));
        await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 });
        _server.Respond(Responses.Rest(File1000, from: 400));

        await DownloadAsync();

        Assert.Equal("Wed, 21 Oct 2026 07:28:00 GMT", _server.Requests[^1].Headers!["If-Range"]);
    }

    [Fact]
    public async Task TheRestOfAFileOfAnotherLengthStartsOver()
    {
        // The link gives another file now, such as a "latest release" link, and the server takes the range anyway:
        // its rest would follow the first file's bytes.
        byte[] newer = [.. Responses.SampleFile(1200).Reverse()];
        _server.Respond(Responses.Whole(File1000, stopAfter: 400));
        await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 });
        _server.Respond(Responses.Rest(newer, from: 400)).Respond(Responses.Whole(newer));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(newer, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task AFileOfAnotherLengthIsntTakenAsComplete()
    {
        // "Range not satisfiable" for a file as long as the bytes kept, but the file started was longer.
        byte[] shorter = [.. Responses.SampleFile(400).Reverse()];
        _server.Respond(Responses.Whole(File1000, stopAfter: 400));
        await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 });
        _server.Respond(Responses.RangeNotSatisfiable(400)).Respond(Responses.Whole(shorter));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(shorter, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task LeavesNothingButTheFileOnceItsSaved()
    {
        _server.Respond(Responses.Whole(File1000, stopAfter: 400, etag: "\"v1\""));
        await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 });
        _server.Respond(Responses.Rest(File1000, from: 400, etag: "\"v1\""));

        await DownloadAsync();

        Assert.Equal([Destination], Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task AWebPageIsntSavedAsTheFile()
    {
        // Such as a sign-in page, or a file host's page about the file rather than the file.
        _server.Respond(Responses.Page("<html>Sign in</html>"));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.LinkExpired, outcome);
        Assert.Empty(Directory.GetFiles(_folder.Path));
        Assert.Contains(_logger.Entries, entry => entry.Contains("answered with a web page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWebPageInPlaceOfTheRestKeepsThePartialFile()
    {
        // A captive portal on a hotel's network, say.
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Page("<html>Accept the terms</html>"));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.LinkExpired, outcome);
        Assert.Equal(File1000[..400], File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task AWebPageIsSavedWhenThatsWhatTheFileIs()
    {
        _server.Respond(Responses.Page("<html>Read me</html>"));
        string page = Path.Combine(_folder.Path, "ReadMe.html");

        TransferOutcome outcome = await CreateTransfer().DownloadAsync(Ticket(), page, _progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal("<html>Read me</html>", File.ReadAllText(page));
    }

    [Fact]
    public async Task AFileHeldForAMomentIsStillMovedIntoPlace()
    {
        // Such as by a virus scanner looking at the finished file.
        int tries = 0;
        _server.Respond(Responses.Whole(File1000));
        HttpFileTransfer transfer = CreateTransfer(move: (from, to) =>
        {
            if (++tries < 3)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            File.Move(from, to);
        });

        TransferOutcome outcome = await transfer.DownloadAsync(Ticket(), Destination, _progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(3, tries);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task AFileHeldForGoodIsAStorageErrorAndKeptWhole()
    {
        _server.Respond(Responses.Whole(File1000));
        HttpFileTransfer transfer = CreateTransfer(move: (_, _) => throw new IOException("The file is in use."));

        TransferOutcome outcome = await transfer.DownloadAsync(Ticket(), Destination, _progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task APartialFileThatCantBeDiscardedIsAStorageError()
    {
        File.WriteAllBytes(PartialFile, Responses.SampleFile(1200));
        _server.Respond(Responses.RangeNotSatisfiable(1000));
        HttpFileTransfer transfer = CreateTransfer(delete: _ => throw new IOException("The file is in use."));

        TransferOutcome outcome = await transfer.DownloadAsync(Ticket(), Destination, _progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: Couldn't write SkyUI_5_2_SE.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResumesAfterTheConnectionDrops()
    {
        _server
            .Respond(Responses.Whole(File1000, stopAfter: 500, ending: StreamEnding.Drop))
            .Respond(Responses.Rest(File1000, from: 500));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Equal("bytes=500-", _server.Requests[1].Range);
        Assert.Single(_progress.Events, e => e.StartsWith("Started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResumesAfterTheConnectionStalls()
    {
        _server
            .Respond(Responses.Whole(File1000, stopAfter: 300, ending: StreamEnding.Stall))
            .Respond(Responses.Rest(File1000, from: 300));

        TransferOutcome outcome = await DownloadAsync(new FileTransferOptions { ReadTimeout = TimeSpan.FromMilliseconds(200) });

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
        Assert.Equal("bytes=300-", _server.Requests[1].Range);
    }

    [Fact]
    public async Task ResumesWhenTheBodyEndsBeforeTheWholeFileArrived()
    {
        _server
            .Respond(Responses.Whole(File1000, stopAfter: 600))
            .Respond(Responses.Rest(File1000, from: 600));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task GivesUpAfterTooManyInterruptionsButKeepsThePartialFile()
    {
        _server.RespondAlways(Responses.Whole(File1000, stopAfter: 100, ending: StreamEnding.Drop));

        TransferOutcome outcome = await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 2 });

        Assert.Equal(TransferOutcome.NetworkError, outcome);
        Assert.Equal(3, _server.Requests.Count);
        Assert.True(File.Exists(PartialFile));
        Assert.False(File.Exists(Destination));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task ReportsAnExpiredLinkAndKeepsThePartialFile(HttpStatusCode status)
    {
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Status(status));

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.LinkExpired, outcome);
        Assert.Equal(File1000[..400], File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task ReportsServerErrorsAsNetworkErrors()
    {
        _server.Respond(Responses.Status(HttpStatusCode.ServiceUnavailable));

        Assert.Equal(TransferOutcome.NetworkError, await DownloadAsync());
    }

    [Fact]
    public async Task ReportsConnectionFailuresAsNetworkErrors()
    {
        _server.Throw(new HttpRequestException("No such host is known."));

        Assert.Equal(TransferOutcome.NetworkError, await DownloadAsync());
    }

    [Theory]
    [InlineData(FileHashAlgorithm.Md5, "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData(FileHashAlgorithm.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData(FileHashAlgorithm.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData(FileHashAlgorithm.Sha512, "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public async Task AcceptsAFileThatMatchesItsHash(FileHashAlgorithm algorithm, string digestOfAbc)
    {
        _server.Respond(Responses.Whole("abc"u8.ToArray()));

        TransferOutcome outcome = await DownloadAsync(expectedHash: new FileHash(algorithm, digestOfAbc));

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal("abc"u8.ToArray(), File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task DiscardsAFileThatDoesNotMatchItsHash()
    {
        _server.Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync(
            expectedHash: new FileHash(FileHashAlgorithm.Sha256, new string('0', 64)));

        Assert.Equal(TransferOutcome.HashMismatch, outcome);
        Assert.False(File.Exists(PartialFile));
        Assert.False(File.Exists(Destination));
        Assert.Equal("Verifying", _progress.Events[^1]);
    }

    [Fact]
    public async Task NeverOverwritesAnExistingFile()
    {
        File.WriteAllText(Destination, "Mine.");

        TransferOutcome outcome = await DownloadAsync();

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Equal("Mine.", File.ReadAllText(Destination));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task NeverOverwritesAFileThatAppearsDuringTheDownload()
    {
        _server.Respond(Responses.Whole(File1000));
        RecordingProgress progress = new() { OnVerifying = () => File.WriteAllText(Destination, "Mine.") };

        TransferOutcome outcome = await CreateTransfer().DownloadAsync(
            Ticket(), Destination, progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Equal("Mine.", File.ReadAllText(Destination));
        Assert.Equal(File1000, File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task ReportsAStorageErrorWhenTheFolderCannotBeCreated()
    {
        string blocker = Path.Combine(_folder.Path, "Downloads");
        File.WriteAllText(blocker, "A file where a folder should be.");
        string destination = Path.Combine(blocker, "Skyrim", "SkyUI_5_2_SE.7z");

        TransferOutcome outcome = await CreateTransfer().DownloadAsync(
            Ticket(), destination, _progress, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task SendsTheTicketsHeaders()
    {
        _server.Respond(Responses.Whole(File1000));

        await DownloadAsync(headers: new Dictionary<string, string> { ["Authorization"] = "Bearer abc" });

        Assert.Equal("Bearer abc", Assert.Single(_server.Requests).Authorization);
    }

    [Fact]
    public async Task KeepsThePartialFileWhenCancelled()
    {
        _server.Respond(Responses.Whole(File1000, stopAfter: 300, ending: StreamEnding.Stall));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<TransferOutcome> download = CreateTransfer().DownloadAsync(Ticket(), Destination, _progress, cancellation.Token);
        await WaitUntilAsync(() => _progress.LastAdvanced == 300);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Equal(File1000[..300], File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task DeletePartialFileRemovesOnlyThePartialFile()
    {
        File.WriteAllText(Destination, "Finished.");
        File.WriteAllText(PartialFile, "Half");
        HttpFileTransfer transfer = CreateTransfer();

        await transfer.DeletePartialFileAsync(Destination, TestContext.Current.CancellationToken);
        await transfer.DeletePartialFileAsync(Destination, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(PartialFile));
        Assert.Equal("Finished.", File.ReadAllText(Destination));
    }

    [Fact]
    public async Task DeletePartialFileRemovesWhatWasKeptOfIt()
    {
        _server.Respond(Responses.Whole(File1000, stopAfter: 400, etag: "\"v1\""));
        await DownloadAsync(new FileTransferOptions { MaxResumeAttempts = 0 });

        await CreateTransfer().DeletePartialFileAsync(Destination, TestContext.Current.CancellationToken);

        Assert.Empty(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task DeletePartialFileIgnoresAFolderThatDoesntExist()
    {
        HttpFileTransfer transfer = CreateTransfer();

        await transfer.DeletePartialFileAsync(
            Path.Combine(Path.GetDirectoryName(Destination)!, "never-created", "SkyUI.7z"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportsProgressAtMostOncePerInterval()
    {
        _server.Respond(Responses.Whole(File1000, chunkSize: 100));

        // The fake clock never moves, so after the first report only the final one gets through.
        await DownloadAsync(timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        long[] expected = [100, 1000];
        Assert.Equal(expected, _progress.Advances);
    }

    [Fact]
    public async Task NeverLogsTheUrlOrTheHeaders()
    {
        _server
            .Respond(Responses.Whole(File1000, stopAfter: 500, ending: StreamEnding.Drop))
            .Respond(Responses.Status(HttpStatusCode.Forbidden));

        await DownloadAsync(headers: new Dictionary<string, string> { ["Authorization"] = "Bearer abc" });

        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains(UrlSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("Bearer", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, entry => entry.Contains("cdn.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesAFileTheDiskHasNoRoomFor()
    {
        _server.Respond(Responses.Whole(File1000));
        List<string> asked = [];

        TransferOutcome outcome = await DownloadAsync(freeSpace: folder =>
        {
            asked.Add(folder);
            return 999;
        });

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Equal([_folder.Path], asked);
        Assert.False(File.Exists(PartialFile));
        Assert.False(File.Exists(Destination));
        Assert.Contains(
            "Warning: There isn't room on the disk for SkyUI_5_2_SE.7z: it needs 1000 more bytes, and 999 are free",
            _logger.Entries.Select(entry => entry.TrimEnd()));
    }

    [Fact]
    public async Task KeepsThePartialFileWhenTheRestHasNoRoom()
    {
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Rest(File1000, from: 400));

        TransferOutcome outcome = await DownloadAsync(freeSpace: _ => 599);

        Assert.Equal(TransferOutcome.StorageError, outcome);
        Assert.Equal(File1000[..400], File.ReadAllBytes(PartialFile));
    }

    [Fact]
    public async Task ResumingNeedsRoomForTheRestOnly()
    {
        File.WriteAllBytes(PartialFile, File1000[..400]);
        _server.Respond(Responses.Rest(File1000, from: 400));

        TransferOutcome outcome = await DownloadAsync(freeSpace: _ => 600);

        Assert.Equal(TransferOutcome.Completed, outcome);
    }

    [Fact]
    public async Task CountsThePartialFileTheWholeFileReplacesAsRoom()
    {
        File.WriteAllBytes(PartialFile, Enumerable.Repeat((byte)0xFF, 400).ToArray());
        _server.Respond(Responses.Whole(File1000)).Respond(Responses.Whole(File1000));

        TransferOutcome fits = await DownloadAsync(freeSpace: _ => 600);
        File.Delete(Destination);
        File.WriteAllBytes(PartialFile, Enumerable.Repeat((byte)0xFF, 400).ToArray());
        TransferOutcome doesnt = await DownloadAsync(freeSpace: _ => 599);

        Assert.Equal((TransferOutcome.Completed, TransferOutcome.StorageError), (fits, doesnt));
    }

    [Fact]
    public async Task DownloadsWhenTheSystemCantTellTheRoom()
    {
        _server.Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync(freeSpace: _ => null);

        Assert.Equal(TransferOutcome.Completed, outcome);
    }

    [Fact]
    public async Task DownloadsAFileOfUnknownSizeWithoutAskingForRoom()
    {
        _server.Respond(() =>
        {
            HttpResponseMessage response = Responses.Whole(File1000)();
            response.Content.Headers.ContentLength = null;
            return response;
        });

        TransferOutcome outcome = await DownloadAsync(freeSpace: _ => 0);

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(File1000, File.ReadAllBytes(Destination));
    }

    private static DownloadTicket Ticket(FileHash? expectedHash = null, IReadOnlyDictionary<string, string>? headers = null)
        => new(new Uri($"https://cdn.example.com/files/SkyUI_5_2_SE.7z?{UrlSecret}"), headers, expectedHash);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The condition never became true.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }
    }

    /// <param name="freeSpace">The disk's room, for a folder; null for the system's own disk.</param>
    /// <param name="move">Moves the finished file into place, in place of the system; null for the system's.</param>
    /// <param name="delete">Deletes a partial file, in place of the system; null for the system's.</param>
    private HttpFileTransfer CreateTransfer(
        FileTransferOptions? options = null,
        TimeProvider? timeProvider = null,
        Func<string, long?>? freeSpace = null,
        Action<string, string>? move = null,
        Action<string>? delete = null)
        => freeSpace is null && move is null && delete is null
            ? new(new SingleHandlerHttpClientFactory(_server), timeProvider ?? TimeProvider.System, options ?? new FileTransferOptions(), _logger)
            : new(
                new SingleHandlerHttpClientFactory(_server),
                timeProvider ?? TimeProvider.System,
                options ?? new FileTransferOptions(),
                _logger,
                freeSpace ?? FreeSpace.Available,
                move,
                delete);

    private Task<TransferOutcome> DownloadAsync(
        FileTransferOptions? options = null,
        FileHash? expectedHash = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeProvider? timeProvider = null,
        Func<string, long?>? freeSpace = null)
        => CreateTransfer(options, timeProvider, freeSpace).DownloadAsync(
            Ticket(expectedHash, headers),
            Destination,
            _progress,
            TestContext.Current.CancellationToken);
}
