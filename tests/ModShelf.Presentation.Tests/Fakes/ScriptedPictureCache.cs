using System.Collections.Concurrent;

using ModShelf.Application.Pictures;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Saved pictures that aren't on disk: each picture's file is its address's last segment in a pictures folder, unless the
/// test says it can't be downloaded. It records the pictures asked for.
/// </summary>
internal sealed class ScriptedPictureCache : IPictureCache
{
    private readonly ConcurrentQueue<Uri> _asked = new();

    /// <summary>
    /// The pictures that can't be downloaded.
    /// </summary>
    public ConcurrentDictionary<Uri, bool> Missing { get; } = new();

    /// <summary>
    /// Holds each picture until set; null to give it at once.
    /// </summary>
    public TaskCompletionSource? Held { get; set; }

    public long Size { get; set; }

    public int Clears { get; private set; }

    /// <summary>
    /// What clearing throws while set.
    /// </summary>
    public Exception? ClearRefusal { get; set; }

    public IReadOnlyList<Uri> Asked => [.. _asked];

    /// <summary>
    /// The file the picture is saved as.
    /// </summary>
    public static string FileOf(Uri picture) => Path.Combine(TestData.Folder, "pictures", picture.Segments[^1]);

    public async Task<string?> GetAsync(Uri picture, CancellationToken cancellationToken)
    {
        _asked.Enqueue(picture);
        if (Held is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return Missing.ContainsKey(picture) ? null : FileOf(picture);
    }

    public Task<long> GetSizeAsync(CancellationToken cancellationToken) => Task.FromResult(Size);

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (ClearRefusal is { } refusal)
        {
            return Task.FromException(refusal);
        }

        Clears++;
        Size = 0;
        return Task.CompletedTask;
    }
}
