using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// Watching the browser's downloads folder while a download waits on a site's page, and handing it the file that's its
/// (ADR 0091).
/// </summary>
public sealed class BrowserFilesTests : IAsyncDisposable
{
    private const string Sha256 = "4e6ec40ee934696a7de4a03e2a043380a68e1b2bfd1ec9134886d0745b86e4ea";

    private static readonly ModFileRef File = TestData.File(TestData.Nexus, "35407");
    private static readonly FakeUserFolders Folders = new();

    private readonly RecordingDownloadService _downloads = new();
    private readonly ScriptedBrowserDownloads _browser = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly ResolverWithFiles _site = new(new ScriptedResolver(TestData.Nexus));
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<BrowserFiles> _logger = new();
    private readonly BrowserFiles _files;

    public BrowserFilesTests()
    {
        SettingsService settings = new(_store, Folders, new InMemoryLibraryRepository(), new InMemoryDownloadJobRepository([]));
        _files = new BrowserFiles(_downloads, _browser, Folders, settings, new SourceRegistry([_site]), _storage, new FakeTimeProvider(TestData.Now), _logger);
    }

    [Fact]
    public async Task WatchesTheSystemsDownloadsFolderOnlyWhileADownloadWaitsOnAPage()
    {
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = Job("35407.7z", DownloadJobStatus.Downloading);
        _downloads.Update(job);
        await SettleAsync();
        Assert.Empty(_browser.Watched);

        _downloads.Update(job with { Status = DownloadJobStatus.AwaitingHandoff });
        await Until(() => _browser.Watching == 1);
        _downloads.Update(job with { Status = DownloadJobStatus.Completed });

        await Until(() => _browser.Watching == 0);
        Assert.Equal([Folders.Downloads], _browser.Watched);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Watching the browser's downloads folder for 1 downloads", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WatchesTheFolderTheSettingsName()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Elsewhere");
        _store.Settings = new AppSettings { BrowserDownloadsFolder = folder };
        await _files.StartAsync(TestContext.Current.CancellationToken);

        _downloads.Update(Job("35407.7z", DownloadJobStatus.AwaitingHandoff));

        await Until(() => _browser.Watching == 1);
        Assert.Equal([folder], _browser.Watched);
    }

    [Fact]
    public async Task ADownloadWaitingBeforeTheWatchBeganIsWatchedFor()
    {
        DownloadJobSnapshot waiting = Job("35407.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Add(waiting);

        await _files.StartAsync(TestContext.Current.CancellationToken);

        await Until(() => _browser.Watching == 1);
    }

    [Theory]
    [InlineData("SkyUI_5_2_SE.7z")]
    [InlineData("skyui_5_2_se.7Z")]
    [InlineData("SkyUI_5_2_SE (1).7z")]
    [InlineData("SkyUI_5_2_SE(12).7z")]
    public async Task HandsAWaitingDownloadTheFileWithItsName(string saved)
    {
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot other = Job("Other.zip", DownloadJobStatus.AwaitingHandoff);
        DownloadJobSnapshot job = Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Update(other);
        _downloads.Update(job);
        await Until(() => _browser.Watching == 1);
        string path = Path.Combine(Folders.Downloads, saved);

        _browser.Appear(path);

        await Until(() => _downloads.TakenFiles.Count == 1);
        Assert.Equal([(job.Id, path)], _downloads.TakenFiles);
        Assert.Empty(_storage.Hashed);
        Assert.Contains(_logger.Entries, entry => entry.Contains($"The browser saved {saved}, for download job {job.Id}: Taken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileSeveralDownloadsWaitForGoesToTheOneThatStartedWaitingLast()
    {
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot first = Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff);
        DownloadJobSnapshot last = Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Update(first);
        _downloads.Update(last);
        _downloads.Update(first);
        await Until(() => _browser.Watching == 1);
        string path = Path.Combine(Folders.Downloads, "SkyUI_5_2_SE.7z");

        _browser.Appear(path);

        await Until(() => _downloads.TakenFiles.Count == 1);
        Assert.Equal([(last.Id, path)], _downloads.TakenFiles);
    }

    [Fact]
    public async Task HandsAWaitingDownloadTheFileWithTheSitesSha256WhateverItsName()
    {
        _site.Files.Add(new ModFile(
            File,
            "Main file",
            "Main File.zip",
            "1.0",
            ModFileCategory.Main,
            null,
            TestData.Now,
            [new FileHash(FileHashAlgorithm.Md5, "900150983cd24fb0d6963f7d28e17f72"), new FileHash(FileHashAlgorithm.Sha256, Sha256.ToUpperInvariant())]));
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = Job("Main File.zip", DownloadJobStatus.AwaitingHandoff) with { File = File };
        _downloads.Update(job);
        await Until(() => _browser.Watching == 1);
        string other = Path.Combine(Folders.Downloads, "photo.jpg");
        string path = Path.Combine(Folders.Downloads, Sha256 + ".zip");
        _storage.Add(other);
        _storage.Add(path);
        _storage.Hashes[path] = Sha256;

        // The site's SHA-256 is asked for meanwhile: a file appearing before it's known is hashed for nothing.
        await SettleAsync();
        _browser.Appear(other);
        _browser.Appear(path);

        await Until(() => _downloads.TakenFiles.Count == 1);
        Assert.Equal([(job.Id, path)], _downloads.TakenFiles);
        Assert.Equal([other, path], _storage.Hashed);
    }

    [Fact]
    public async Task LeavesAFileOfAnotherNameAloneWhenNoSiteSaysAHash()
    {
        await _files.StartAsync(TestContext.Current.CancellationToken);
        _downloads.Update(Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff));
        await Until(() => _browser.Watching == 1);

        _browser.Appear(Path.Combine(Folders.Downloads, "SkyUI_5_2_SE.zip"));
        _browser.Appear(Path.Combine(Folders.Downloads, "SkyUI.7z"));
        await SettleAsync();

        Assert.Empty(_downloads.TakenFiles);
        Assert.Empty(_storage.Hashed);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task LeavesAFileItCantReadAloneAndGoesOn(Type failure)
    {
        _site.Files.Add(new ModFile(File, "Main file", "Main File.zip", "1.0", ModFileCategory.Main, null, TestData.Now, [new FileHash(FileHashAlgorithm.Sha256, Sha256)]));
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = Job("Main File.zip", DownloadJobStatus.AwaitingHandoff) with { File = File };
        _downloads.Update(job);
        await Until(() => _browser.Watching == 1);
        await SettleAsync();
        string locked = Path.Combine(Folders.Downloads, "locked.zip");
        _storage.Add(locked);
        _storage.Failures[locked] = (Exception)Activator.CreateInstance(failure, "Another program has it.")!;
        string path = Path.Combine(Folders.Downloads, "Main File.zip");

        _browser.Appear(locked);
        _browser.Appear(path);

        await Until(() => _downloads.TakenFiles.Count == 1);
        Assert.Equal([(job.Id, path)], _downloads.TakenFiles);
        Assert.Equal([locked], _storage.Hashed);
    }

    [Fact]
    public async Task ADownloadThatStoppedWaitingMeanwhileIsPassedOver()
    {
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot stopped = Job("Stopped.zip", DownloadJobStatus.AwaitingHandoff);
        DownloadJobSnapshot waiting = Job("Waiting.zip", DownloadJobStatus.AwaitingHandoff);
        _downloads.Update(stopped);
        _downloads.Update(waiting);
        await Until(() => _browser.Watching == 1);
        _downloads.TakeOutcome = (id, _) => id == stopped.Id
            ? throw new InvalidOperationException("The download job is Cancelled: only one waiting on a site's page takes a file.")
            : TakeFileOutcome.Taken;
        string first = Path.Combine(Folders.Downloads, "Stopped.zip");
        string second = Path.Combine(Folders.Downloads, "Waiting.zip");

        _browser.Appear(first);
        _browser.Appear(second);

        await Until(() => _downloads.TakenFiles.Count == 2);
        Assert.Equal([(stopped.Id, first), (waiting.Id, second)], _downloads.TakenFiles);
        await Until(() => _logger.Entries.Any(entry => entry.Contains($"for download job {waiting.Id}: Taken", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task KeepsWatchingWhenAFileIsntTheOne()
    {
        _downloads.TakeOutcome = (_, path) => path.EndsWith(" (1).7z", StringComparison.Ordinal) ? TakeFileOutcome.Taken : TakeFileOutcome.HashMismatch;
        await _files.StartAsync(TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Update(job);
        await Until(() => _browser.Watching == 1);

        _browser.Appear(Path.Combine(Folders.Downloads, "SkyUI_5_2_SE.7z"));
        _browser.Appear(Path.Combine(Folders.Downloads, "SkyUI_5_2_SE (1).7z"));

        await Until(() => _downloads.TakenFiles.Count == 2);
        Assert.Contains(_logger.Entries, entry => entry.Contains("The browser saved SkyUI_5_2_SE.7z, for download job " + job.Id + ": HashMismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaysWhenTheFolderCantBeWatched()
    {
        _browser.Failure = new DirectoryNotFoundException("The browser's downloads folder isn't there.");
        await _files.StartAsync(TestContext.Current.CancellationToken);

        _downloads.Update(Job("SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff));

        await Until(() => _logger.Entries.Any(entry => entry.Contains("can't be watched", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("mod.zip", "mod.zip", true)]
    [InlineData("mod.zip", "MOD.ZIP", true)]
    [InlineData("mod.zip", "mod (1).zip", true)]
    [InlineData("mod.zip", "mod(2).zip", true)]
    [InlineData("mod", "mod (3)", true)]
    [InlineData("mod (1).zip", "mod (1).zip", true)]
    [InlineData("mod (1).zip", "mod (1) (1).zip", true)]
    [InlineData("mod.zip", "mod (x).zip", false)]
    [InlineData("mod.zip", "mod ().zip", false)]
    [InlineData("mod.zip", "mod.7z", false)]
    [InlineData("mod.zip", "mod  (1).zip", false)]
    [InlineData("mod.zip", "amod.zip", false)]
    [InlineData("mod.zip", " (1).zip", false)]
    public void ABrowsersNameIsTheFilesWithOrWithoutANumber(string fileName, string saved, bool same)
    {
        Assert.Equal(same, BrowserFiles.SameName(fileName, saved));
    }

    public async ValueTask DisposeAsync()
    {
        await _files.StopAsync(CancellationToken.None);
        _files.Dispose();
        _downloads.Stop();
    }

    private static DownloadJobSnapshot Job(string fileName, DownloadJobStatus status)
        => RecordingDownloadService.Job(File, TestData.TargetDirectory, fileName, status) with { File = TestData.File(TestData.Nexus, fileName) };

    private static async Task Until(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>
    /// Gives the watcher a moment to handle what it was sent.
    /// </summary>
    private static Task SettleAsync() => Task.Delay(200, TestContext.Current.CancellationToken);
}
