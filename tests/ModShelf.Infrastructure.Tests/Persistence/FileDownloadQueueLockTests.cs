using ModShelf.Infrastructure.Persistence;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// The download queue's lock beside the library database. Two locks on the same database stand in for two processes:
/// each opens the file on its own, as another process would.
/// </summary>
public sealed class FileDownloadQueueLockTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private LibraryDatabaseOptions Database => new(Path.Combine(_folder.Path, "data", "library.db"));

    [Fact]
    public void OneHolderAtATime()
    {
        using FileDownloadQueueLock first = new(Database);
        using FileDownloadQueueLock second = new(Database);

        bool firstTook = first.TryAcquire();
        bool secondTookToo = second.TryAcquire();
        first.Release();
        bool secondTookAfter = second.TryAcquire();

        Assert.Equal((true, false, true), (firstTook, secondTookToo, secondTookAfter));
        Assert.False(first.TryAcquire());
    }

    [Fact]
    public void TakingItAgainKeepsIt()
    {
        using FileDownloadQueueLock queueLock = new(Database);

        Assert.True(queueLock.TryAcquire());
        Assert.True(queueLock.TryAcquire());
    }

    [Fact]
    public void ItSitsBesideTheDatabaseAndStaysThere()
    {
        using FileDownloadQueueLock queueLock = new(Database);

        queueLock.TryAcquire();
        queueLock.Release();
        queueLock.Release();

        Assert.True(File.Exists(Database.DatabasePath + ".downloads.lock"));
    }

    [Fact]
    public void DisposingLetsItGo()
    {
        using FileDownloadQueueLock other = new(Database);
        FileDownloadQueueLock first = new(Database);
        first.TryAcquire();

        first.Dispose();

        Assert.True(other.TryAcquire());
    }
}
