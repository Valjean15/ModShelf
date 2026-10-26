using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Watchlist;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site with a watchlist, kept in memory. It recognizes every link as a file of <see cref="LinkedMod"/>, or none. The
/// test can make any of its answers fail. It records what it was asked.
/// </summary>
internal sealed class FakeWatchlistSource(SourceId id, bool recognizesLinks = true) : ILinkRecognizer, IWatchlistSync
{
    private readonly ConcurrentQueue<string> _asked = new();
    private readonly ConcurrentDictionary<ModRef, bool> _watched = new();

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public ModRef LinkedMod => TestData.File(Id, "35407").Mod;

    /// <summary>
    /// What the next answer of each kind throws instead, keyed by the method's name.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new();

    /// <summary>
    /// The methods called so far, in order.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
        => Answer(nameof(RecognizeAsync), () => recognizesLinks ? new RecognizedLink(TestData.File(Id, "35407"), "SkyUI_5_2_SE.7z") : null);

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken)
        => Answer(nameof(GetWatchedAsync), () => (IReadOnlyList<ModRef>)[.. _watched.Keys]);

    public Task WatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => Answer(nameof(WatchAsync), () => _watched[modRef] = true);

    public Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => Answer(nameof(UnwatchAsync), () => _watched.TryRemove(modRef, out _));

    private Task<T> Answer<T>(string method, Func<T> answer)
    {
        _asked.Enqueue(method);
        return Failures.TryRemove(method, out Exception? failure) ? Task.FromException<T>(failure) : Task.FromResult(answer());
    }
}
