using System.Collections.Concurrent;
using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Watchlist;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The watchlist, kept in memory: at first mods 7, 120 and 1000 of its game, and mod 5 of "othergame". Tracking or
/// untracking mod 503 fails, and with <see cref="WatchlistNeedsAccount"/> set every question about it does.
/// </summary>
internal sealed partial class FakeCatalogSite : IWatchlistSync
{
    private readonly ConcurrentDictionary<ModRef, bool> _tracked = new(
        new[] { ("game", "7"), ("game", "120"), ("game", "1000"), ("othergame", "5") }
            .Select(mod => KeyValuePair.Create(new ModRef(new GameRef(SourceId, mod.Item1), mod.Item2), true)));

    /// <summary>
    /// Whether the watchlist refuses for want of an account.
    /// </summary>
    public bool WatchlistNeedsAccount { get; set; }

    public IReadOnlyCollection<ModRef> Tracked => [.. _tracked.Keys];

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken)
    {
        RefuseWithoutAccount();
        return Task.FromResult<IReadOnlyList<ModRef>>([.. _tracked.Keys]);
    }

    public Task WatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        RefuseToChange(modRef);
        _tracked[modRef] = true;
        return Task.CompletedTask;
    }

    public Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        RefuseToChange(modRef);
        _tracked.TryRemove(modRef, out _);
        return Task.CompletedTask;
    }

    private void RefuseToChange(ModRef modRef)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        if (modRef.Game.Source != SourceId)
        {
            throw new ArgumentException("The mod isn't this site's.", nameof(modRef));
        }

        RefuseWithoutAccount();
        if (modRef.Id == "503")
        {
            throw new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private void RefuseWithoutAccount()
    {
        if (WatchlistNeedsAccount)
        {
            throw new AuthenticationFailedException("The site wants an account first.");
        }
    }
}
