using System.Diagnostics;
using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// Phase 1's promise, end to end on each operating system: the built CLI, run as its own process against a local
/// server, can be stopped part-way or killed outright, and running the same command again finishes the file from
/// where it stopped and verifies it.
/// </summary>
public sealed class DownloadCommandTests : IAsyncDisposable
{
    private const string FileName = "mod.zip";

    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Interrupted = 130;

    // Not a multiple of any buffer size, so the download resumes wherever the bytes happened to stop.
    private const long StopsAt = (5 * 1024 * 1024) + 12345;

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);
    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024 * 1024);

    private readonly ITestOutputHelper _log;
    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, FileName);

    public DownloadCommandTests(ITestOutputHelper log) => _log = log;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Database => Path.Combine(_folder.Path, "library.db");

    private string Downloads => Path.Combine(_folder.Path, "Downloads");

    private string Destination => Path.Combine(Downloads, FileName);

    private string PartialFile => Destination + ".part";

    private string[] DownloadCommand => ["download", $"{_server.FileUrl}#sha256={HashOf(Content)}", "--to", Downloads];

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task AKilledDownloadContinuesFromItsPartialFile()
    {
        _server.HoldAt(StopsAt);
        using (CliProcess cli = CliProcess.Start(Database, DownloadCommand))
        {
            await WaitForPartialFileAsync(cli);
            cli.Kill();
            Log(await cli.WaitForExitAsync(StepTimeout, Cancellation));
        }

        Assert.Equal(StopsAt, PartialLength());
        Assert.False(File.Exists(Destination));

        _server.SendEverything();
        CliResult resumed = await RunAsync(DownloadCommand);

        Assert.Equal(Done, resumed.ExitCode);
        Assert.Contains("Continuing the download", resumed.Output, StringComparison.Ordinal);
        await AssertFinishedFromThePartialFileAsync();
    }

    [Fact]
    public async Task CtrlCStopsTheDownloadAndTheSameCommandContinuesIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows can't send Ctrl+C to a process that doesn't share the test's console; check it there by hand.");

        _server.HoldAt(StopsAt);
        CliResult stopped;
        using (CliProcess cli = CliProcess.Start(Database, DownloadCommand))
        {
            await WaitForPartialFileAsync(cli);
            await cli.InterruptAsync(Cancellation);
            stopped = Log(await cli.WaitForExitAsync(StepTimeout, Cancellation));
        }

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Equal(StopsAt, PartialLength());
        Assert.False(File.Exists(Destination));
        Assert.Contains("Queued", (await RunAsync(["queue", "list"])).QueueRowFor(FileName), StringComparison.Ordinal);

        _server.SendEverything();
        CliResult resumed = await RunAsync(DownloadCommand);

        Assert.Equal(Done, resumed.ExitCode);
        Assert.Contains("Continuing the download", resumed.Output, StringComparison.Ordinal);
        await AssertFinishedFromThePartialFileAsync();
    }

    [Fact]
    public async Task TheSameCommandAfterTheFileIsSavedSaysSo()
    {
        CliResult saved = await RunAsync(DownloadCommand);

        CliResult again = await RunAsync(DownloadCommand);

        Assert.Equal((Done, Done), (saved.ExitCode, again.ExitCode));
        Assert.Contains($"{Destination} is saved already.", again.Output, StringComparison.Ordinal);
        Assert.Equal([null], _server.Ranges);
        string queue = (await RunAsync(["queue", "list"])).Output;
        Assert.Single(queue.Split('\n'), line => line.Contains(FileName, StringComparison.Ordinal));
        Assert.DoesNotContain("Failed", queue, StringComparison.Ordinal);
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private long PartialLength() => File.Exists(PartialFile) ? new FileInfo(PartialFile).Length : 0;

    /// <summary>
    /// Waits until the partial file holds every byte the server sent before holding the connection.
    /// </summary>
    private async Task WaitForPartialFileAsync(CliProcess cli)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (PartialLength() != StopsAt)
        {
            if (cli.HasExited)
            {
                CliResult ended = Log(await cli.WaitForExitAsync(StepTimeout, Cancellation));
                Assert.Fail($"The CLI ended before the download got going. {ended}");
            }

            if (waited.Elapsed > StepTimeout)
            {
                Assert.Fail($"The partial file still holds {PartialLength()} of the {StopsAt} bytes sent.");
            }

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

    private async Task AssertFinishedFromThePartialFileAsync()
    {
        string?[] ranges = [null, $"bytes={StopsAt}-"];
        Assert.Equal(ranges, _server.Ranges);
        Assert.Equal(HashOf(Content), HashOf(await File.ReadAllBytesAsync(Destination, Cancellation)));
        Assert.False(File.Exists(PartialFile));
        Assert.Contains("Completed", (await RunAsync(["queue", "list"])).QueueRowFor(FileName), StringComparison.Ordinal);
    }
}
