using System.Diagnostics;
using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// Two CLI processes on one library, as when a second download command starts while the first one, or the desktop app,
/// runs the downloads: one process at a time runs the queue, and the other leaves its jobs alone.
/// </summary>
public sealed class DownloadCommandTwoProcessTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;

    private const long StopsAt = (2 * 1024 * 1024) + 4321;

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);
    private static readonly byte[] FirstContent = RandomNumberGenerator.GetBytes(8 * 1024 * 1024);
    private static readonly byte[] SecondContent = RandomNumberGenerator.GetBytes(1024 * 1024);

    private readonly ITestOutputHelper _log;
    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _first = new(FirstContent, "first.zip");
    private readonly RangeFileServer _second = new(SecondContent, "second.zip");

    public DownloadCommandTwoProcessTests(ITestOutputHelper log) => _log = log;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Database => Path.Combine(_folder.Path, "library.db");

    private string Downloads => Path.Combine(_folder.Path, "Downloads");

    private string FirstPartialFile => Path.Combine(Downloads, "first.zip.part");

    public async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task ASecondDownloadRefusesWhileTheFirstRunsTheQueueAndLeavesItsJobAlone()
    {
        _first.HoldAt(StopsAt);
        using CliProcess first = CliProcess.Start(Database, ["download", _first.FileUrl.AbsoluteUri, "--to", Downloads]);
        await WaitForFirstPartialFileAsync(first);

        CliResult second = await RunAsync(["download", _second.FileUrl.AbsoluteUri, "--to", Downloads]);
        string firstRow = (await RunAsync(["queue", "list"])).QueueRowFor("first.zip");
        _first.SendEverything();
        _first.DropHeldConnections();
        CliResult firstDone = Log(await first.WaitForExitAsync(StepTimeout, Cancellation));
        CliResult secondAgain = await RunAsync(["download", _second.FileUrl.AbsoluteUri, "--to", Downloads]);

        Assert.Equal(Failed, second.ExitCode);
        Assert.Contains("Another ModShelf process is running the downloads of this library", second.Errors, StringComparison.Ordinal);
        Assert.Contains("Downloading", firstRow, StringComparison.Ordinal);
        Assert.Equal(Done, firstDone.ExitCode);
        Assert.Equal([null, $"bytes={StopsAt}-"], _first.Ranges);
        Assert.Equal(Done, secondAgain.ExitCode);
        Assert.Equal([null], _second.Ranges);
        Assert.Equal(FirstContent, await File.ReadAllBytesAsync(Path.Combine(Downloads, "first.zip"), Cancellation));
    }

    [Fact]
    public async Task AKilledProcessLetsTheNextOneRunTheQueue()
    {
        _first.HoldAt(StopsAt);
        using (CliProcess first = CliProcess.Start(Database, ["download", _first.FileUrl.AbsoluteUri, "--to", Downloads]))
        {
            await WaitForFirstPartialFileAsync(first);
            first.Kill();
            Log(await first.WaitForExitAsync(StepTimeout, Cancellation));
        }

        CliResult second = await RunAsync(["download", _second.FileUrl.AbsoluteUri, "--to", Downloads]);

        Assert.Equal(Done, second.ExitCode);
        Assert.Equal(SecondContent, await File.ReadAllBytesAsync(Path.Combine(Downloads, "second.zip"), Cancellation));
    }

    private long FirstPartialLength() => File.Exists(FirstPartialFile) ? new FileInfo(FirstPartialFile).Length : 0;

    private async Task WaitForFirstPartialFileAsync(CliProcess cli)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (FirstPartialLength() != StopsAt)
        {
            if (cli.HasExited)
            {
                CliResult ended = Log(await cli.WaitForExitAsync(StepTimeout, Cancellation));
                Assert.Fail($"The CLI ended before the download got going. {ended}");
            }

            Assert.True(waited.Elapsed < StepTimeout, $"The partial file still holds {FirstPartialLength()} of the {StopsAt} bytes sent.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        }
    }

    private async Task<CliResult> RunAsync(string[] arguments)
    {
        using CliProcess cli = CliProcess.Start(Database, arguments);
        return Log(await cli.WaitForExitAsync(StepTimeout, Cancellation));
    }

    private CliResult Log(CliResult result)
    {
        _log.WriteLine(result.ToString());
        return result;
    }
}
