using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Each source's credentials, kept in the <see cref="ISecretStore"/> under <c>account/&lt;source&gt;</c> as JSON, with an
/// in-memory copy and one lock per source.
/// </summary>
/// <remarks>
/// <para>
/// The vault itself doesn't lock: callers hold <see cref="LockAsync"/> around anything that reads the store or
/// changes an entry, so linking, unlinking and refreshing one source never interleave in this process.
/// </para>
/// <para>
/// Another ModShelf process can link, unlink or refresh the same account, so the copy in memory is read again once it's
/// <see cref="ReadAgainAfter"/> old, and whenever a caller asks (<see cref="ReloadAsync"/>), as before a refresh. Credentials
/// kept in memory only, because the store couldn't take them, aren't read again: the store has older ones.
/// </para>
/// </remarks>
internal sealed partial class CredentialVault(ISecretStore store, TimeProvider timeProvider, ILogger logger)
{
    /// <summary>
    /// How long the copy in memory is used before the store is read again.
    /// </summary>
    internal static readonly TimeSpan ReadAgainAfter = TimeSpan.FromMinutes(1);

    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<SourceId, SemaphoreSlim> _locks = new();

    // A source with no entry here hasn't been read yet; a null entry in one means there's nothing stored.
    private readonly ConcurrentDictionary<SourceId, Cached> _cache = new();

    public async Task<IDisposable> LockAsync(SourceId source, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(source, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    /// <summary>
    /// The in-memory entry, when the source's credentials were read recently enough to use. Safe without the lock.
    /// </summary>
    public bool TryGetCached(SourceId source, out VaultEntry? entry)
    {
        bool fresh = _cache.TryGetValue(source, out Cached? cached) && IsFresh(cached);
        entry = fresh ? cached!.Entry : null;
        return fresh;
    }

    /// <summary>
    /// The source's entry: from memory when it was read recently enough, otherwise from the store. Credentials that
    /// can't be read count as none. Call while holding the source's lock.
    /// </summary>
    /// <exception cref="Exception">The store can't be read, and nothing was read from it before.</exception>
    public Task<VaultEntry?> GetAsync(SourceId source, CancellationToken cancellationToken)
        => _cache.TryGetValue(source, out Cached? cached) && IsFresh(cached)
            ? Task.FromResult(cached.Entry)
            : ReloadAsync(source, cancellationToken);

    /// <summary>
    /// The source's entry as the store has it now, since another process may have changed it; or the one kept in
    /// memory only. Credentials found to need linking again still do when the store has the same ones. When the store
    /// can't be read, which is logged, the entry read before stays. Call while holding the source's lock.
    /// </summary>
    /// <exception cref="Exception">The store can't be read, and nothing was read from it before.</exception>
    public async Task<VaultEntry?> ReloadAsync(SourceId source, CancellationToken cancellationToken)
    {
        _cache.TryGetValue(source, out Cached? cached);
        if (cached is { InMemoryOnly: true })
        {
            return cached.Entry;
        }

        VaultEntry? entry;
        try
        {
            entry = await ReadAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (cached is not null && exception is not OperationCanceledException)
        {
            LogCantReadAgain(logger, exception, source);
            _cache[source] = cached with { ReadAt = timeProvider.GetUtcNow() };
            return cached.Entry;
        }

        if (cached?.Entry is { NeedsRelink: true } known && entry is not null && entry.Kind == known.Kind && entry.Credentials == known.Credentials)
        {
            entry = known;
        }

        _cache[source] = new Cached(entry, timeProvider.GetUtcNow(), InMemoryOnly: false);
        return entry;
    }

    /// <summary>
    /// Stores the entry, replacing the source's earlier one. Call while holding the source's lock.
    /// </summary>
    public async Task SaveAsync(SourceId source, VaultEntry entry, CancellationToken cancellationToken)
    {
        StoredCredentials stored = new(
            FormatVersion,
            entry.Kind,
            entry.Credentials.AccessToken,
            entry.Credentials.RefreshToken,
            entry.Credentials.ExpiresAt);
        await store.WriteAsync(NameOf(source), JsonSerializer.Serialize(stored, Json), cancellationToken).ConfigureAwait(false);
        _cache[source] = new Cached(entry, timeProvider.GetUtcNow(), InMemoryOnly: false);
    }

    /// <summary>
    /// Keeps the entry in memory only, until ModShelf closes or it's saved, for when it can't be stored. Call while
    /// holding the source's lock.
    /// </summary>
    public void Keep(SourceId source, VaultEntry entry) => _cache[source] = new Cached(entry, timeProvider.GetUtcNow(), InMemoryOnly: true);

    /// <summary>
    /// Removes the source's credentials. Call while holding the source's lock.
    /// </summary>
    public async Task DeleteAsync(SourceId source, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(NameOf(source), cancellationToken).ConfigureAwait(false);
        _cache[source] = new Cached(null, timeProvider.GetUtcNow(), InMemoryOnly: false);
    }

    /// <summary>
    /// Remembers, until the account is linked again, that the source's credentials can't be refreshed.
    /// Call while holding the source's lock.
    /// </summary>
    public void MarkNeedsRelink(SourceId source)
    {
        if (_cache.TryGetValue(source, out Cached? cached) && cached.Entry is not null)
        {
            _cache[source] = cached with { Entry = cached.Entry with { NeedsRelink = true } };
        }
    }

    private static string NameOf(SourceId source) => $"account/{source.Value}";

    private bool IsFresh(Cached cached) => cached.InMemoryOnly || timeProvider.GetUtcNow() - cached.ReadAt < ReadAgainAfter;

    private async Task<VaultEntry?> ReadAsync(SourceId source, CancellationToken cancellationToken)
    {
        string? json;
        try
        {
            json = await store.ReadAsync(NameOf(source), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            LogUnreadable(logger, source);
            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            StoredCredentials? stored = JsonSerializer.Deserialize<StoredCredentials>(json, Json);
            if (stored is { Version: FormatVersion } && Enum.IsDefined(stored.Kind))
            {
                return new VaultEntry(stored.Kind, new AccountCredentials(stored.AccessToken, stored.RefreshToken, stored.ExpiresAt));
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // Falls through: the exception's message could quote the stored JSON, so it isn't logged.
        }

        LogUnreadable(logger, source);
        return null;
    }

    /// <summary>
    /// The secret's JSON. Versioned, so a later format can still read this one.
    /// </summary>
    private sealed record StoredCredentials(
        int Version,
        AuthKind Kind,
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset? ExpiresAt);

    /// <summary>
    /// A source's entry in memory: null when nothing is stored.
    /// </summary>
    /// <param name="ReadAt">When it was read from the store, or saved there.</param>
    /// <param name="InMemoryOnly">Whether the store couldn't take it, so it isn't read again.</param>
    private sealed record Cached(VaultEntry? Entry, DateTimeOffset ReadAt, bool InMemoryOnly);

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
