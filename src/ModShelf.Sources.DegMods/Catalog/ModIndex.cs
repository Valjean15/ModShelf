using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// DEG Mods' mods as its relays hold them, and its admin's moderation applied. Every mod is read at once, in pages of
/// 500, for Browse, its searches and the games list, and kept a few minutes; a mod asked for by itself, as its page, a
/// link and an update check ask, is read afresh.
/// </summary>
internal sealed class ModIndex : IDisposable
{
    /// <summary>
    /// How many events a page of every mod holds: the most DEG Mods' relays send at once.
    /// </summary>
    internal const int PageSize = 500;

    private readonly DegModsRelays _relays;
    private readonly string _admin;
    private readonly TimeSpan _keepFor;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _reading = new(1, 1);

    private Snapshot? _mods;
    private (Moderation Moderation, DateTimeOffset ReadAt)? _moderation;

    public ModIndex(DegModsRelays relays, string admin, TimeSpan keepFor, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentException.ThrowIfNullOrWhiteSpace(admin);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _relays = relays;
        _admin = admin;
        _keepFor = keepFor;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Every mod DEG Mods shows, read within the last few minutes.
    /// </summary>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<IReadOnlyList<DegMod>> AllAsync(CancellationToken cancellationToken)
    {
        if (Fresh(_mods?.ReadAt) && _mods is { } kept)
        {
            return kept.Mods;
        }

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Fresh(_mods?.ReadAt) && _mods is { } read)
            {
                return read.Mods;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            IReadOnlyList<NostrEvent> events = await _relays.ReadAsync([.. ModFilters(since: null), .. ModerationFilters()], cancellationToken).ConfigureAwait(false);
            Moderation moderation = Moderation.Read(events, _admin);
            _moderation = (moderation, now);
            _mods = new Snapshot(moderation.Apply(ModEvents.Read(events)), now);
            return _mods.Mods;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>
    /// The mod of that author with that <c>d</c> tag, read afresh, and whether DEG Mods shows it.
    /// </summary>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<(ModStatus Status, DegMod? Mod)> FindAsync(string author, string d, CancellationToken cancellationToken)
    {
        NostrFilter mod = new()
        {
            Kinds = [DegModsLink.ModKind, DegModsLink.OldModKind],
            Authors = [author],
            Tags = new Dictionary<char, IReadOnlyList<string>> { ['d'] = [d] },
        };
        (Moderation moderation, IReadOnlyList<NostrEvent> events) = await ReadWithModerationAsync([mod], cancellationToken).ConfigureAwait(false);
        DegMod? found = ModEvents.Read(events.Where(nostrEvent => nostrEvent.PubKey == author && nostrEvent.D == d)).SingleOrDefault();
        return found is null ? (ModStatus.Missing, null)
            : moderation.Hides(found) ? (ModStatus.Hidden, null)
            : (ModStatus.Shown, moderation.Apply(found));
    }

    /// <summary>
    /// The mods changed since a time, read afresh, that DEG Mods shows.
    /// </summary>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<IReadOnlyList<DegMod>> ChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        (Moderation moderation, IReadOnlyList<NostrEvent> events) = await ReadWithModerationAsync(ModFilters(since), cancellationToken).ConfigureAwait(false);
        return moderation.Apply(ModEvents.Read(events));
    }

    public void Dispose() => _reading.Dispose();

    /// <summary>
    /// What the filters find, and the moderation: kept when read within the last few minutes, or read with them.
    /// </summary>
    private async Task<(Moderation Moderation, IReadOnlyList<NostrEvent> Events)> ReadWithModerationAsync(
        IReadOnlyList<NostrFilter> filters, CancellationToken cancellationToken)
    {
        if (Fresh(_moderation?.ReadAt) && _moderation is { } kept)
        {
            return (kept.Moderation, await _relays.ReadAsync(filters, cancellationToken).ConfigureAwait(false));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<NostrEvent> events = await _relays.ReadAsync([.. filters, .. ModerationFilters()], cancellationToken).ConfigureAwait(false);
        Moderation moderation = Moderation.Read(events, _admin);
        _moderation = (moderation, now);
        return (moderation, events);
    }

    /// <summary>
    /// Every mod, or those changed since a time: the new kind's, and the older listings from before Aug 1, 2026.
    /// </summary>
    private static NostrFilter[] ModFilters(DateTimeOffset? since)
    {
        long? from = since?.ToUnixTimeSeconds();
        NostrFilter mods = new() { Kinds = [DegModsLink.ModKind], Since = from, Limit = PageSize };
        NostrFilter oldMods = new()
        {
            Kinds = [DegModsLink.OldModKind],
            Tags = new Dictionary<char, IReadOnlyList<string>> { ['t'] = [ModEvents.OldModTag] },
            Since = from,
            Until = ModEvents.OldModsEnd - 1,
            Limit = PageSize,
        };
        return from >= ModEvents.OldModsEnd ? [mods] : [mods, oldMods];
    }

    /// <summary>
    /// The admin's lists, and its moderation tags.
    /// </summary>
    private NostrFilter[] ModerationFilters()
        =>
        [
            new()
            {
                Kinds = [Moderation.ListKind],
                Authors = [_admin],
                Tags = new Dictionary<char, IReadOnlyList<string>> { ['d'] = [Moderation.HiddenTagsList, Moderation.BlockedModsList] },
            },
            new() { Kinds = [Moderation.BlockedAuthorsKind], Authors = [_admin] },
            new()
            {
                Kinds = [Moderation.MarkKind],
                Authors = [_admin],
                Tags = new Dictionary<char, IReadOnlyList<string>> { ['L'] = [Moderation.Label] },
                Limit = PageSize,
            },
        ];

    private bool Fresh(DateTimeOffset? readAt)
        => readAt is { } at && _timeProvider.GetUtcNow() - at < _keepFor && _timeProvider.GetUtcNow() >= at;

    /// <summary>
    /// Every mod DEG Mods shows, and when they were read.
    /// </summary>
    private sealed record Snapshot(IReadOnlyList<DegMod> Mods, DateTimeOffset ReadAt);
}
