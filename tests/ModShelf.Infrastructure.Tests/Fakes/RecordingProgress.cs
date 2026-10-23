using System.Collections.Concurrent;
using System.Globalization;

using ModShelf.Application.Downloads;

namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// Records what a transfer reports, as short strings such as "Started 0/1000", "Advanced 400" and "Verifying".
/// </summary>
internal sealed class RecordingProgress : ITransferProgress
{
    private readonly ConcurrentQueue<string> _events = new();
    private long _lastAdvanced;

    /// <summary>
    /// Runs when the transfer reports <see cref="Verifying"/>, so a test can act just before the file is moved into place.
    /// </summary>
    public Action? OnVerifying { get; init; }

    public IReadOnlyList<string> Events => [.. _events];

    public long LastAdvanced => Interlocked.Read(ref _lastAdvanced);

    public IReadOnlyList<long> Advances
        => [.. _events.Where(e => e.StartsWith("Advanced ", StringComparison.Ordinal)).Select(e => long.Parse(e[9..], CultureInfo.InvariantCulture))];

    public void Started(long resumeFromByte, long? totalBytes)
        => _events.Enqueue(string.Create(CultureInfo.InvariantCulture, $"Started {resumeFromByte}/{totalBytes?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));

    public void Advanced(long bytesDownloaded)
    {
        Interlocked.Exchange(ref _lastAdvanced, bytesDownloaded);
        _events.Enqueue(string.Create(CultureInfo.InvariantCulture, $"Advanced {bytesDownloaded}"));
    }

    public void Verifying()
    {
        _events.Enqueue("Verifying");
        OnVerifying?.Invoke();
    }
}
