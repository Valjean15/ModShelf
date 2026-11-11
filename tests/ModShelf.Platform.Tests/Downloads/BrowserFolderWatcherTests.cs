using ModShelf.Platform.Downloads;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Downloads;

/// <summary>
/// The files a browser finishes in the folder it saves downloads to, on this system's file watcher (ADR 0091).
/// </summary>
public sealed class BrowserFolderWatcherTests : IDisposable
{
    // Short, so the tests are quick; long enough for a write in parts to look unfinished between them.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<BrowserFolderWatcher> _logger = new();
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task GivesAFileWrittenInPlaceOnceItsSizeStopsChanging()
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");

        await File.WriteAllBytesAsync(path, new byte[1000], TestContext.Current.CancellationToken);

        Assert.Equal(path, await watch.NextAsync());
        Assert.Contains(_logger.Entries, entry => entry.Contains("Watching the browser's downloads folder", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains(_folder.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitsWhileAFileWrittenInPlaceStillGrows()
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");

        // A pause of more than the settle period is one quiet tick: its size is noted, and it's given once it keeps it.
        await File.WriteAllBytesAsync(path, new byte[1000], TestContext.Current.CancellationToken);
        await Task.Delay(Settle * 1.5, TestContext.Current.CancellationToken);
        bool givenWhileGrowing = watch.HasNext;
        await using (FileStream file = new(path, FileMode.Append))
        {
            await file.WriteAsync(new byte[1000], TestContext.Current.CancellationToken);
        }

        Assert.False(givenWhileGrowing);
        Assert.Equal(path, await watch.NextAsync());
        Assert.Equal(2000, new FileInfo(path).Length);
    }

    [Fact]
    public async Task AnEmptyFileIsntGivenUntilItHasContent()
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");

        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        await Task.Delay(Settle * 4, TestContext.Current.CancellationToken);
        bool givenEmpty = watch.HasNext;
        await File.WriteAllBytesAsync(path, new byte[10], TestContext.Current.CancellationToken);

        Assert.False(givenEmpty);
        Assert.Equal(path, await watch.NextAsync());
    }

    [Theory]
    [InlineData(".crdownload")]
    [InlineData(".part")]
    [InlineData(".download")]
    public async Task GivesAFileOnceTheBrowserRenamesItFromItsOwnName(string ending)
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");
        string unfinished = path + ending;
        await File.WriteAllBytesAsync(unfinished, new byte[1000], TestContext.Current.CancellationToken);
        await Task.Delay(Settle * 3, TestContext.Current.CancellationToken);
        Assert.False(watch.HasNext);

        File.Move(unfinished, path);

        Assert.Equal(path, await watch.NextAsync());
    }

    [Fact]
    public async Task WaitsWhileAFileBesideItIsUnfinishedAsFirefoxLeavesOne()
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");

        // Firefox leaves an empty file under the final name, writes beside it, then moves that over it.
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path + ".part", new byte[1000], TestContext.Current.CancellationToken);
        await Task.Delay(Settle * 3, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, new byte[10], TestContext.Current.CancellationToken);
        await Task.Delay(Settle * 3, TestContext.Current.CancellationToken);
        Assert.False(watch.HasNext);

        File.Move(path + ".part", path, overwrite: true);

        Assert.Equal(path, await watch.NextAsync());
        await Task.Delay(Settle * 3, TestContext.Current.CancellationToken);
        Assert.False(watch.HasNext);
    }

    [Fact]
    public async Task GivesEachFileOnceThoughItChangesAfter()
    {
        Watch watch = Start();
        string path = Path.Combine(_folder.Path, "mod.zip");
        await File.WriteAllBytesAsync(path, new byte[1000], TestContext.Current.CancellationToken);
        Assert.Equal(path, await watch.NextAsync());

        await File.WriteAllBytesAsync(path, new byte[2000], TestContext.Current.CancellationToken);
        string other = Path.Combine(_folder.Path, "other.7z");
        await File.WriteAllBytesAsync(other, new byte[10], TestContext.Current.CancellationToken);

        Assert.Equal(other, await watch.NextAsync());
        Assert.False(watch.HasNext);
    }

    [Fact]
    public async Task LeavesFilesInFoldersInsideItAndOnesGoneAlone()
    {
        Watch watch = Start();
        string inside = Path.Combine(_folder.Path, "inside");
        Directory.CreateDirectory(inside);
        await File.WriteAllBytesAsync(Path.Combine(inside, "mod.zip"), new byte[1000], TestContext.Current.CancellationToken);
        string gone = Path.Combine(_folder.Path, "gone.zip");
        await File.WriteAllBytesAsync(gone, new byte[1000], TestContext.Current.CancellationToken);
        File.Delete(gone);

        await Task.Delay(Settle * 4, TestContext.Current.CancellationToken);

        Assert.False(watch.HasNext);
    }

    [Fact]
    public async Task AFolderThatIsntThereCantBeWatched()
    {
        BrowserFolderWatcher watcher = new(TimeProvider.System, _logger, Settle);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (string _ in watcher.WatchAsync(Path.Combine(_folder.Path, "none"), _stop.Token))
            {
            }
        });
    }

    [Theory]
    [InlineData("mod.zip.crdownload", true)]
    [InlineData("Unconfirmed 123.crdownload", true)]
    [InlineData("mod.zip.PART", true)]
    [InlineData("mod.zip.partial", true)]
    [InlineData("mod.zip.tmp", true)]
    [InlineData("mod.zip.opdownload", true)]
    [InlineData("mod.zip", false)]
    [InlineData("mod.part.zip", false)]
    public void ABrowsersOwnNameIsUnfinished(string name, bool unfinished)
    {
        Assert.Equal(unfinished, BrowserFolderWatcher.IsUnfinishedName(name));
    }

    private Watch Start()
    {
        BrowserFolderWatcher watcher = new(TimeProvider.System, _logger, Settle);
        return new Watch(watcher.WatchAsync(_folder.Path, _stop.Token).GetAsyncEnumerator(_stop.Token));
    }

    /// <summary>
    /// A watch going on: the next file it gives, and whether it has given one not yet taken.
    /// </summary>
    private sealed class Watch
    {
        private readonly IAsyncEnumerator<string> _files;
        private Task<bool> _next;

        public Watch(IAsyncEnumerator<string> files)
        {
            _files = files;
            _next = files.MoveNextAsync().AsTask();
        }

        public bool HasNext => _next.IsCompletedSuccessfully && _next.Result;

        public async Task<string> NextAsync()
        {
            Assert.True(await _next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            string file = _files.Current;
            _next = _files.MoveNextAsync().AsTask();
            return file;
        }
    }
}
