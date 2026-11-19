using ModShelf.Application.Contents;
using ModShelf.Application.Contents.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Archives as the test says: each path's entries and files' bytes, or what reading it throws.
/// </summary>
internal sealed class ScriptedArchiveReader : IArchiveReader
{
    public Dictionary<string, IReadOnlyList<ArchiveEntry>> Entries { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string Path, string Entry), byte[]> Files { get; } = [];

    /// <summary>
    /// What listing or reading any archive throws; null for nothing.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// The entries read, as asked for.
    /// </summary>
    public List<string> Read { get; } = [];

    public string? FormatOf(string fileName) => Path.GetExtension(fileName).ToUpperInvariant() switch
    {
        ".ZIP" => "zip",
        ".7Z" => "7z",
        ".RAR" => "rar",
        _ => null,
    };

    public Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken)
        => Failure is { } failure ? Task.FromException<IReadOnlyList<ArchiveEntry>>(failure) : Task.FromResult(Entries[path]);

    public Task<ArchiveBytes> ReadAsync(string path, string entry, int maxBytes, CancellationToken cancellationToken)
    {
        Read.Add(entry);
        if (Failure is { } failure)
        {
            return Task.FromException<ArchiveBytes>(failure);
        }

        return Files.TryGetValue((path, entry), out byte[]? bytes)
            ? Task.FromResult(new ArchiveBytes(bytes.AsMemory(0, Math.Min(bytes.Length, maxBytes)), bytes.Length))
            : Task.FromException<ArchiveBytes>(new KeyNotFoundException($"{Path.GetFileName(path)} has no file {entry}."));
    }
}
