using System.Diagnostics;
using System.Security.Cryptography;

using Microsoft.Data.Sqlite;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>download --to</c> in-process while what it depends on fails: the library database locked by another program for a
/// while, and a disk that fills up.
/// </summary>
public sealed class DownloadCommandFailureTests : IAsyncDisposable
{
    private const string FileName = "mod.zip";

    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;

    private const long StopsAt = (1024 * 1024) + 4321;

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);
    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);

    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, FileName);
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public DownloadCommandFailureTests()
    {
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Downloads => Path.Combine(_folder.Path, "Downloads");

    private string Destination => Path.Combine(Downloads, FileName);

    private string PartialFile => Destination + ".part";

    private string[] DownloadCommand => ["download", $"{_server.FileUrl}#sha256={Convert.ToHexStringLower(SHA256.HashData(Content))}", "--to", Downloads];

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task ADatabaseAnotherProgramHoldsForAWhileOnlyDelaysTheDownload()
    {
        _server.HoldAt(StopsAt);
        await using CliRun run = _cli.Start(DownloadCommand, TextReader.Null);
        await WaitForPartialFileAsync(StopsAt);

        await using (SqliteConnection other = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_cli.Folder, "library.db"), Pooling = false }.ToString()))
        {
            await other.OpenAsync(Cancellation);
            await using SqliteCommand hold = other.CreateCommand();
            hold.CommandText = "BEGIN IMMEDIATE";
            await hold.ExecuteNonQueryAsync(Cancellation);

            _server.SendEverything();
            _server.DropHeldConnections();
            await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);

            // The file is in place, but the finished job is told only once it's saved, which waits for the database.
            Assert.True(File.Exists(Destination));
            Assert.DoesNotContain("Saved", run.Output, StringComparison.Ordinal);
            hold.CommandText = "COMMIT";
            await hold.ExecuteNonQueryAsync(Cancellation);
        }

        CliResult done = await run.WaitForExitAsync(Cancellation);

        Assert.Equal(Done, done.ExitCode);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Destination, Cancellation));
        Assert.Contains("Completed", (await _cli.RunAsync(["queue", "list"], Cancellation)).QueueRowFor(FileName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiskThatFillsUpFailsTheDownloadAndSaysWhy()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && File.Exists("/dev/full"), "Only Linux has /dev/full, a file every write to fails as a full disk does.");
        Directory.CreateDirectory(Downloads);
        File.CreateSymbolicLink(PartialFile, "/dev/full");

        CliResult failed = await _cli.RunAsync(DownloadCommand, Cancellation);

        Assert.Equal(Failed, failed.ExitCode);
        Assert.Contains("The download failed: the file couldn't be saved.", failed.Errors, StringComparison.Ordinal);
        Assert.False(File.Exists(Destination));
        Assert.Contains("Failed", (await _cli.RunAsync(["queue", "list"], Cancellation)).QueueRowFor(FileName), StringComparison.Ordinal);
    }

    private async Task WaitForPartialFileAsync(long length)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!File.Exists(PartialFile) || new FileInfo(PartialFile).Length != length)
        {
            Assert.True(waited.Elapsed < StepTimeout, "The partial file never held the bytes sent.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        }
    }
}
