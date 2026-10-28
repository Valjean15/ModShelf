using System.Collections.Concurrent;

using ModShelf.Application.Watchlist;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The tracked mods of the sites the test says, kept as a site keeps them, recording what each was asked.
/// </summary>
internal sealed class ScriptedWatchlists : IWatchlists
{
    private readonly ConcurrentQueue<string> _asked = new();

    public List<SourceId> Tracking { get; } = [];

    public IReadOnlyList<SourceId> Sources => [.. Tracking];

    /// <summary>
    /// The mods tracked, of every site.
    /// </summary>
    public HashSet<ModRef> Tracked { get; } = [];

    /// <summary>
    /// What the next calls throw, by method name, one each.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Refusals { get; } = new();

    /// <summary>
    /// What was asked, in order, such as "GetWatchedAsync nexus" or "WatchAsync 12604".
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(SourceId source, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"GetWatchedAsync {source}");
        return Refused(nameof(GetWatchedAsync)) is { } refusal
            ? Task.FromException<IReadOnlyList<ModRef>>(refusal)
            : Task.FromResult<IReadOnlyList<ModRef>>([.. Tracked.Where(mod => mod.Game.Source == source)]);
    }

    public Task WatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"WatchAsync {modRef.Id}");
        if (Refused(nameof(WatchAsync)) is { } refusal)
        {
            return Task.FromException(refusal);
        }

        Tracked.Add(modRef);
        return Task.CompletedTask;
    }

    public Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"UnwatchAsync {modRef.Id}");
        if (Refused(nameof(UnwatchAsync)) is { } refusal)
        {
            return Task.FromException(refusal);
        }

        Tracked.Remove(modRef);
        return Task.CompletedTask;
    }

    public Task<ModRef> WatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ModRef> UnwatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    private Exception? Refused(string method) => Refusals.TryRemove(method, out Exception? refusal) ? refusal : null;
}
