using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The download queue's lock: a file next to the library database, held open with no sharing. Windows refuses a second
/// open of it, and on Linux and macOS .NET takes an exclusive advisory lock on it, so no other ModShelf process can
/// hold it at the same time. The operating system lets go when the process ends, however it ends. The file stays, empty.
/// </summary>
/// <remarks>
/// On Linux and macOS, .NET skips the advisory lock when the environment variable
/// <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c> is set, and so does any process that isn't ModShelf's.
/// </remarks>
internal sealed class FileDownloadQueueLock : IDownloadQueueLock, IDisposable
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private FileStream? _held;

    public FileDownloadQueueLock(LibraryDatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _path = database.DatabasePath + ".downloads.lock";
    }

    /// <exception cref="UnauthorizedAccessException">The lock file can't be created, as in a folder this user can't write to.</exception>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_held is not null)
            {
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            try
            {
                _held = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                // Another process holds it.
                return false;
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _held?.Dispose();
            _held = null;
        }
    }

    public void Dispose() => Release();
}
