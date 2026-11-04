using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Common;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A disk the test says what's on, holding paths up to <see cref="MaxPath"/> characters long. Moves and deletes can
/// fail the way the test says.
/// </summary>
internal sealed class FakeLibraryStorage : ILibraryStorage
{
    private readonly ConcurrentDictionary<string, long> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);

    public int MaxPath { get; set; } = int.MaxValue;

    /// <summary>
    /// What moving, deleting or reading the file at a path throws instead.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs after each move, with where the file went.
    /// </summary>
    public Action<string>? AfterMove { get; set; }

    /// <summary>
    /// Runs before each delete, with the path deleted.
    /// </summary>
    public Action<string>? BeforeDelete { get; set; }

    /// <summary>
    /// Every move, in order.
    /// </summary>
    public ConcurrentQueue<(string Source, string Destination)> Moves { get; } = new();

    /// <summary>
    /// Every folder removed, in order.
    /// </summary>
    public ConcurrentQueue<string> RemovedFolders { get; } = new();

    public IReadOnlyCollection<string> Files => [.. _files.Keys];

    /// <summary>
    /// Folders that lead to the same one, as a link does, both ways round. Otherwise folders are the same when their
    /// paths are, as this system compares them.
    /// </summary>
    public List<(string First, string Second)> SameFolders { get; } = [];

    /// <summary>
    /// Puts a file at <paramref name="path"/>, and notes its folders up to the system's root as folders that can be
    /// removed once empty.
    /// </summary>
    public void Add(string path, long size = 100)
    {
        _files[path] = size;
        for (string? folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            _folders.Add(folder);
        }
    }

    public void Remove(string path) => _files.TryRemove(path, out _);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool IsSameFolder(string first, string second)
        => FilePaths.Same(first, second) || SameFolders.Contains((first, second)) || SameFolders.Contains((second, first));

    public long? FileSize(string path) => _files.TryGetValue(path, out long size) ? size : null;

    /// <summary>
    /// The drives' roots, each a drive of its own; a path under none of them is on the system's drive.
    /// </summary>
    public List<string> Drives { get; } = [];

    /// <summary>
    /// The free space on each drive, by its root, and on the system's drive by the empty string; unknown otherwise.
    /// </summary>
    public Dictionary<string, long> Free { get; } = new(StringComparer.Ordinal);

    public long? FreeSpace(string folder) => Free.TryGetValue(DriveOf(folder), out long free) ? free : null;

    public bool SameDrive(string first, string second) => DriveOf(first) == DriveOf(second);

    /// <summary>
    /// What listing the folder at a path throws instead.
    /// </summary>
    public ConcurrentDictionary<string, Exception> ListFailures { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> FilesIn(string path)
    {
        if (ListFailures.TryGetValue(path, out Exception? failure))
        {
            throw failure;
        }

        string folder = path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        return [.. _files.Keys
            .Where(file => file.StartsWith(folder, StringComparison.Ordinal) && file.IndexOf(Path.DirectorySeparatorChar, folder.Length) < 0)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The hashes reading a file at a path gives; any other file's is made from its path and size.
    /// </summary>
    public ConcurrentDictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Every file read for its hash, in order.
    /// </summary>
    public ConcurrentQueue<string> Hashed { get; } = new();

    /// <summary>
    /// The hash a file at <paramref name="path"/> has, as <see cref="Sha256Async"/> gives it.
    /// </summary>
    public string HashOf(string path)
        => Hashes.TryGetValue(path, out string? hash)
            ? hash
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{path}:{FileSize(path)}")));

    /// <summary>
    /// Runs before each file is read for its hash, with its path.
    /// </summary>
    public Action<string>? BeforeHash { get; set; }

    public Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        BeforeHash?.Invoke(path);
        Hashed.Enqueue(path);
        if (Failures.TryGetValue(path, out Exception? failure))
        {
            return Task.FromException<string>(failure);
        }

        return _files.ContainsKey(path)
            ? Task.FromResult(HashOf(path))
            : Task.FromException<string>(new FileNotFoundException($"No file at {path}.", path));
    }

    public void MoveFile(string source, string destination)
    {
        if (Failures.TryGetValue(source, out Exception? failure))
        {
            throw failure;
        }

        if (_files.ContainsKey(destination) || !_files.TryRemove(source, out long size))
        {
            throw new IOException($"Can't move {source} to {destination}.");
        }

        // Its folders are made as it moves, and can go once they're empty.
        Add(destination, size);
        Moves.Enqueue((source, destination));
        AfterMove?.Invoke(destination);
    }

    public void DeleteFile(string path)
    {
        BeforeDelete?.Invoke(path);
        if (Failures.TryGetValue(path, out Exception? failure))
        {
            throw failure;
        }

        _files.TryRemove(path, out _);
    }

    public bool RemoveFolderIfEmpty(string path)
    {
        string folder = path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        if (!_folders.Contains(path) || _files.Keys.Any(file => file.StartsWith(folder, StringComparison.Ordinal)))
        {
            return false;
        }

        _folders.Remove(path);
        RemovedFolders.Enqueue(path);
        return true;
    }

    public void EnsurePathFits(string path)
    {
        if (path.Length > MaxPath)
        {
            throw new PathTooLongException($"{path} is too long.");
        }
    }

    private string DriveOf(string path)
        => Drives.Where(root => FilePaths.Same(path, root) || Domain.Library.LibraryPaths.RelativeTo(root, path) is not null)
            .MaxBy(root => root.Length) ?? string.Empty;
}
