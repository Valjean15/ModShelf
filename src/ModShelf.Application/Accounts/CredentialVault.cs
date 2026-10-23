using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Each source's credentials, kept in the <see cref="ISecretStore"/> under <c>account/&lt;source&gt;</c> as JSON, with an
/// in-memory copy and one lock per source.
/// </summary>
/// <remarks>
/// The vault itself doesn't lock: callers hold <see cref="LockAsync"/> around anything that reads the store or
/// changes an entry, so linking, unlinking and refreshing one source never interleave.
/// </remarks>
internal sealed partial class CredentialVault(ISecretStore store, ILogger logger)
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<SourceId, SemaphoreSlim> _locks = new();

    // A source with no entry here hasn't been read yet; a null entry means there's nothing stored.
    private readonly ConcurrentDictionary<SourceId, VaultEntry?> _cache = new();

    public async Task<IDisposable> LockAsync(SourceId source, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(source, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    /// <summary>
    /// The in-memory entry, when the source's credentials were read already. Safe without the lock.
    /// </summary>
    public bool TryGetCached(SourceId source, out VaultEntry? entry) => _cache.TryGetValue(source, out entry);

    /// <summary>
    /// The source's entry, read from the store the first time. Credentials that can't be read count as none.
    /// Call while holding the source's lock.
    /// </summary>
    public async Task<VaultEntry?> GetAsync(SourceId source, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(source, out VaultEntry? cached))
        {
            return cached;
        }

        VaultEntry? entry = await ReadAsync(source, cancellationToken).ConfigureAwait(false);
        _cache[source] = entry;
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
        _cache[source] = entry;
    }

    /// <summary>
    /// Removes the source's credentials. Call while holding the source's lock.
    /// </summary>
    public async Task DeleteAsync(SourceId source, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(NameOf(source), cancellationToken).ConfigureAwait(false);
        _cache[source] = null;
    }

    /// <summary>
    /// Remembers, until the account is linked again, that the source's credentials can't be refreshed.
    /// Call while holding the source's lock.
    /// </summary>
    public void MarkNeedsRelink(SourceId source)
    {
        if (_cache.TryGetValue(source, out VaultEntry? entry) && entry is not null)
        {
            _cache[source] = entry with { NeedsRelink = true };
        }
    }

    private static string NameOf(SourceId source) => $"account/{source.Value}";

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

    [LoggerMessage(EventId = 405, Level = LogLevel.Warning, Message = "The stored credentials for {Source} can't be read; the account has to be linked again")]
    private static partial void LogUnreadable(ILogger logger, SourceId source);

    /// <summary>
    /// The secret's JSON. Versioned, so a later format can still read this one.
    /// </summary>
    private sealed record StoredCredentials(
        int Version,
        AuthKind Kind,
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset? ExpiresAt);

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
