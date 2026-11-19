using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ModShelf.Sources.Thunderstore.Api;

namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// Each community's listing, read through its <c>package-listing-index</c> and kept in memory: read again once it's older
/// than the lifetime, with <c>If-Modified-Since</c>, so an unchanged listing costs one small answer. Callers asking for
/// the same community at once share one read. A read that fails leaves the listing read before in use, and is tried
/// again a few minutes later; with none, it fails the caller (ADR 0103).
/// </summary>
internal sealed partial class ThunderstoreListings : IDisposable
{
    /// <summary>
    /// How long a listing that couldn't be read again is used before it's tried again.
    /// </summary>
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    private readonly ThunderstoreApiClient _api;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly ILogger<ThunderstoreListings> _logger;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ThunderstoreListings(ThunderstoreApiClient api, TimeProvider time, TimeSpan lifetime, ILogger<ThunderstoreListings> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        _api = api;
        _time = time;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// The community's listing, as read within the lifetime, or read now.
    /// </summary>
    /// <param name="community">The community's identifier, such as <c>repo</c>.</param>
    /// <exception cref="HttpRequestException">
    /// The listing couldn't be read, and none was before: 404 for a community Thunderstore doesn't have; with no status
    /// for a listing that isn't as Thunderstore writes them.
    /// </exception>
    public async Task<Listing> GetAsync(string community, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(community);

        Entry entry = _entries.GetOrAdd(community, _ => new Entry());
        if (entry.Current is { } fresh && IsFresh(fresh))
        {
            return fresh;
        }

        await entry.Reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read by another caller while this one waited.
            if (entry.Current is { } read && IsFresh(read))
            {
                return read;
            }

            Listing? before = entry.Current;
            try
            {
                entry.Current = await ReadAsync(community, before, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (before is not null && exception is HttpRequestException)
            {
                LogKeptOld(_logger, exception, community, before.ReadAt);
                entry.Current = before.ReadAgainAt(_time.GetUtcNow() - _lifetime + RetryAfterFailure);
            }

            return entry.Current;
        }
        finally
        {
            entry.Reading.Release();
        }
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values)
        {
            entry.Reading.Dispose();
        }
    }

    /// <summary>
    /// The package a chunk gives, kept small; null for one with no version, or a name Thunderstore doesn't give.
    /// </summary>
    internal static ListedPackage? Compact(ThunderstorePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Versions is not [{ } newest, ..] versions
            || !PackageName.IsVersion(newest.VersionNumber)
            || PackageName.Parse($"{package.Owner}-{package.Name}") is not { } name)
        {
            return null;
        }

        return new ListedPackage(
            name,
            [.. (package.Categories ?? []).Where(category => !string.IsNullOrWhiteSpace(category))],
            package.DateCreated,
            package.DateUpdated,
            Math.Max(package.RatingScore, 0),
            package.IsDeprecated,
            package.HasNsfwContent,
            string.IsNullOrWhiteSpace(newest.Description) ? null : newest.Description.Trim(),
            Uri.TryCreate(newest.Icon, UriKind.Absolute, out Uri? icon) && icon.Scheme == Uri.UriSchemeHttps ? icon : null,
            [.. (newest.Dependencies ?? []).Where(dependency => !string.IsNullOrWhiteSpace(dependency))],
            [
                .. versions
                    .Where(version => PackageName.IsVersion(version.VersionNumber))
                    .Select(version => new ListedVersion(
                        version.VersionNumber,
                        version.DateCreated,
                        Math.Max(version.FileSize, 0),
                        Math.Max(version.Downloads, 0))),
            ]);
    }

    private bool IsFresh(Listing listing) => _time.GetUtcNow() - listing.ReadAt < _lifetime;

    private async Task<Listing> ReadAsync(string community, Listing? before, CancellationToken cancellationToken)
    {
        ListingIndex index = await _api.GetListingIndexAsync(community, before?.LastModified, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _time.GetUtcNow();
        if (index.Unchanged && before is not null)
        {
            LogUnchanged(_logger, community, before.LastModified);
            return before.ReadAgainAt(now);
        }

        // Downloaded a few at once, as the client allows, and parsed in order as each arrives, so the listing keeps
        // Thunderstore's order. One that fails fails the read: a listing with packages missing would have their mods
        // taken for gone from the site.
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<byte[]>[] chunks = [.. index.Chunks.Select(chunk => _api.GetBlobAsync(chunk, reading.Token))];
        List<ListedPackage> packages = [];
        int leftOut = 0;
        try
        {
            foreach (Task<byte[]> chunk in chunks)
            {
                await foreach (ThunderstorePackage? package in Parse(await chunk.ConfigureAwait(false), reading.Token).ConfigureAwait(false))
                {
                    if (package is not null && Compact(package) is { } listed)
                    {
                        packages.Add(listed);
                    }
                    else
                    {
                        leftOut++;
                    }
                }
            }
        }
        catch
        {
            await reading.CancelAsync().ConfigureAwait(false);
            await ((Task)Task.WhenAll(chunks)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        if (leftOut > 0)
        {
            LogLeftOut(_logger, leftOut, community);
        }

        LogRead(_logger, community, packages.Count, index.Chunks.Count);
        return new Listing(community, packages, now, index.LastModified);
    }

    /// <summary>
    /// A chunk's packages, read as the chunk unpacks.
    /// </summary>
    /// <exception cref="HttpRequestException">The chunk isn't a JSON list of packages, or unpacks to too much.</exception>
    private static async IAsyncEnumerable<ThunderstorePackage?> Parse(
        byte[] chunk,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using Stream unpacked = ThunderstoreApiClient.Unpacked(chunk);
        IAsyncEnumerator<ThunderstorePackage?> packages = JsonSerializer
            .DeserializeAsyncEnumerable<ThunderstorePackage>(unpacked, ThunderstoreApiClient.Json, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        await using (packages.ConfigureAwait(false))
        {
            while (true)
            {
                try
                {
                    if (!await packages.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    throw new HttpRequestException(HttpRequestError.InvalidResponse, "A chunk of Thunderstore's listing isn't a list of packages.", exception);
                }

                yield return packages.Current;
            }
        }
    }

    /// <summary>
    /// A community's listing, and the gate its reads go through one at a time.
    /// </summary>
    private sealed class Entry
    {
        public SemaphoreSlim Reading { get; } = new(1, 1);

        public Listing? Current { get; set; }
    }
}
