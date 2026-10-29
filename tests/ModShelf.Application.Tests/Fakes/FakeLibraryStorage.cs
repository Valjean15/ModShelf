using System.Collections.Concurrent;

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
    /// What moving or deleting the file at a path throws instead.
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

        _files[destination] = size;
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
}
