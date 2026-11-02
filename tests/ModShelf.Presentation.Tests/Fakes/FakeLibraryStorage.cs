using ModShelf.Application.Library.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A disk with only the files the test puts on it, which is all a mod's page asks about.
/// </summary>
internal sealed class FakeLibraryStorage : ILibraryStorage
{
    /// <summary>
    /// The files on disk, by full path.
    /// </summary>
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public bool FileExists(string path) => Files.Contains(path);

    public long? FileSize(string path) => throw new NotSupportedException();

    public IReadOnlyList<string> FilesIn(string path) => throw new NotSupportedException();

    public Task<string> Sha256Async(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsSameFolder(string first, string second) => throw new NotSupportedException();

    public void EnsurePathFits(string path) => throw new NotSupportedException();

    public void MoveFile(string source, string destination) => throw new NotSupportedException();

    public void DeleteFile(string path) => throw new NotSupportedException();

    public bool RemoveFolderIfEmpty(string path) => throw new NotSupportedException();
}
