using System.Collections.Concurrent;

using ModShelf.Application.Library.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A disk the test says what's on, holding paths up to <see cref="MaxPath"/> characters long.
/// </summary>
internal sealed class FakeLibraryStorage : ILibraryStorage
{
    private readonly ConcurrentDictionary<string, bool> _files = new(StringComparer.Ordinal);

    public int MaxPath { get; init; } = int.MaxValue;

    public void Add(string path) => _files[path] = true;

    public void Remove(string path) => _files.TryRemove(path, out _);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public void EnsurePathFits(string path)
    {
        if (path.Length > MaxPath)
        {
            throw new PathTooLongException($"{path} is too long.");
        }
    }
}
