using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>download</c> of a file on a page, as DEG Mods' MediaFire links are: the browser downloads it, and ModShelf takes it
/// from the browser's downloads folder, or from the path typed (ADR 0091).
/// </summary>
public sealed class DownloadCommandBrowserFileTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int Waiting = 3;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(64 * 1024);

    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly FakeHandoffSource _source;
    private readonly InProcessCli _cli;

    public DownloadCommandBrowserFileTests()
    {
        _source = new FakeHandoffSource(_server.FileUrl) { HandsOverLinks = false };
        _source.Hashes["1"] = FileHash.FromDigest(FileHashAlgorithm.Sha256, SHA256.HashData(Content));
        _cli = new InProcessCli(_site, _browser, sources: [_source]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Downloads => Path.Combine(_folder.Path, "Downloads");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task SaysTheBrowserDownloadsItAndTakesThePathTyped()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Path: ", Cancellation);
        string downloaded = Saved("handoff-1 (1).zip", Content);

        input.Type($"\"{downloaded}\"");

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Contains("The file is on a page, which only your browser downloads it from.", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Download the file there. ModShelf takes it from {_cli.Downloads} once your browser has saved it, or type its path here", result.Output, StringComparison.Ordinal);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, FileNameOf("1")), Cancellation));
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public async Task AsksAgainForAFileThatIsntTheOne()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Path: ", Cancellation);
        string other = Saved("other.zip", [1, 2, 3]);

        input.Type(other);
        await run.WaitForOutputAsync("checksum isn't the one the site gives", Cancellation);
        input.Type(Path.Combine(_folder.Path, "Browser", "gone.zip"));
        await run.WaitForOutputAsync("There's no file there.", Cancellation);
        input.Type(Saved("handoff-1.zip", Content));

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Contains($"That file isn't {FileNameOf("1")}: its checksum isn't the one the site gives. It's left where it was.", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task TakesTheFileTheBrowserSavesInItsDownloadsFolder()
    {
        Directory.CreateDirectory(_cli.Downloads);
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Path: ", Cancellation);

        await File.WriteAllBytesAsync(Path.Combine(_cli.Downloads, FileNameOf("1")), Content, Cancellation);

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, FileNameOf("1")), Cancellation));
        Assert.Contains(_cli.Logs.Entries, entry => entry.Contains("took the file the browser downloaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANexusModsLikeSiteSaysAFileCanBeHandedOverToo()
    {
        _source.HandsOverLinks = true;
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(Saved("handoff-1.zip", Content));

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Contains($"If your browser downloads the file instead, ModShelf takes it from {_cli.Downloads}, or type its path here.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsWaitingWhenTheInputEndsWithoutAFile()
    {
        CliResult result = await _cli.RunAsync(Download(FileLink("1")), Cancellation);

        Assert.Equal(Waiting, result.ExitCode);
        Assert.Contains("No file was given, so the download is still waiting for the file from its page.", result.Errors, StringComparison.Ordinal);
    }

    private string[] Download(Uri link) => ["download", link.AbsoluteUri, "--to", Downloads];

    /// <summary>
    /// A file the browser saved, in a folder of the test's.
    /// </summary>
    private string Saved(string name, byte[] content)
    {
        string folder = Path.Combine(_folder.Path, "Browser");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
