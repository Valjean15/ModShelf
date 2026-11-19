using System.IO.Compression;
using System.Net;
using System.Text.Json;

using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// Calls Thunderstore's API through the source's HTTP client, a few requests at a time, and turns its answers into
/// results or exceptions the rest of the app understands. Follows the listing index's redirect itself, to Thunderstore's
/// own hosts only, and reads its blobs, gzip-compressed whatever their headers say.
/// </summary>
internal sealed class ThunderstoreApiClient(IHttpClientFactory clients, Uri baseAddress, int maxRequestsAtOnce) : IDisposable
{
    /// <summary>
    /// The most a blob may unpack to: Thunderstore's listing chunks are 14 MB at most.
    /// </summary>
    internal const long MostUnpackedBytes = 64L * 1024 * 1024;

    // Error bodies can be whole HTML pages; only this much of one is kept.
    private const int MaxMessageLength = 300;

    /// <summary>
    /// The API's JSON: names in snake case, such as <c>version_number</c>.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly SemaphoreSlim _atOnce = new(maxRequestsAtOnce, maxRequestsAtOnce);

    /// <summary>
    /// The name of the source's client, which the app sets up with its retries and time limits.
    /// </summary>
    internal static string ClientName { get; } = SourceHttpClients.NameFor(ThunderstoreSource.ThunderstoreId);

    /// <summary>
    /// Thunderstore's address, ending in a slash.
    /// </summary>
    public Uri BaseAddress { get; } = baseAddress;

    /// <summary>
    /// Sends a GET request to <paramref name="relativeUrl"/>, such as <c>api/cyberstorm/community/</c>, and reads the
    /// JSON answer.
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// Thunderstore refused or failed. <see cref="HttpRequestException.StatusCode"/> says which: 404 when what was asked
    /// for doesn't exist; 429 when it asks to slow down; null when it couldn't be reached, didn't answer in time, or
    /// answered with something other than JSON.
    /// </exception>
    public Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);
        return GetAsync<T>(new Uri(BaseAddress, relativeUrl), cancellationToken);
    }

    /// <summary>
    /// As <see cref="GetAsync{T}(string, CancellationToken)"/>, for a link Thunderstore gave, such as a page's next.
    /// </summary>
    /// <exception cref="HttpRequestException">The link isn't to Thunderstore, or as above.</exception>
    public async Task<T> GetAsync<T>(Uri url, CancellationToken cancellationToken)
    {
        RequireOwn(url);
        (HttpStatusCode status, byte[] body, _, _) = await SendAsync(url, null, cancellationToken).ConfigureAwait(false);
        ThrowUnlessSuccess(status, body);
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json)
                ?? throw new HttpRequestException(HttpRequestError.InvalidResponse, "Thunderstore answered with nothing.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "Thunderstore answered with something other than the JSON expected.", exception);
        }
    }

    /// <summary>
    /// Asks a community's listing index, <c>c/{community}/api/v1/package-listing-index/</c>, for its chunks, unless it's
    /// unchanged since <paramref name="since"/>.
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// As <see cref="GetAsync{T}(string, CancellationToken)"/>; 404 for a community Thunderstore doesn't have, 503 while it
    /// hasn't built the community's listing; with no status when the index or a chunk's link isn't Thunderstore's, or the
    /// index isn't a list of links.
    /// </exception>
    public async Task<ListingIndex> GetListingIndexAsync(string community, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(community);

        Uri asked = new(BaseAddress, $"c/{Uri.EscapeDataString(community)}/api/v1/package-listing-index/");
        (HttpStatusCode status, byte[] body, Uri? location, DateTimeOffset? lastModified) =
            await SendAsync(asked, since, cancellationToken).ConfigureAwait(false);
        switch (status)
        {
            case HttpStatusCode.NotModified:
                return new ListingIndex(Unchanged: true, [], lastModified ?? since);
            case HttpStatusCode.ServiceUnavailable:
                throw new HttpRequestException($"Thunderstore hasn't built the listing of {community} yet. Try again in a while.", null, status);
            case HttpStatusCode.NotFound:
                throw new HttpRequestException($"Thunderstore has no community '{community}'.", null, status);
        }

        byte[] index;
        if ((int)status is >= 300 and <= 399)
        {
            Uri blob = location is null
                ? throw new HttpRequestException(HttpRequestError.InvalidResponse, "Thunderstore's listing index pointed nowhere.")
                : location.IsAbsoluteUri ? location : new Uri(asked, location);
            index = await GetBlobAsync(blob, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ThrowUnlessSuccess(status, body);
            index = body;
        }

        IReadOnlyList<string>? links;
        try
        {
            await using Stream unpacked = Unpacked(index);
            links = await JsonSerializer.DeserializeAsync<IReadOnlyList<string>>(unpacked, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "Thunderstore's listing index isn't a list of links.", exception);
        }

        List<Uri> chunks = [];
        foreach (string link in links ?? [])
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? chunk))
            {
                throw new HttpRequestException(HttpRequestError.InvalidResponse, "Thunderstore's listing index holds something other than a link.");
            }

            RequireOwn(chunk);
            chunks.Add(chunk);
        }

        return new ListingIndex(Unchanged: false, chunks, lastModified);
    }

    /// <summary>
    /// Reads a blob of a listing, such as a chunk, from Thunderstore's own hosts.
    /// </summary>
    /// <returns>The blob as it came, which <see cref="Unpacked"/> reads.</returns>
    /// <exception cref="HttpRequestException">The link isn't Thunderstore's, or as <see cref="GetAsync{T}(string, CancellationToken)"/>.</exception>
    public async Task<byte[]> GetBlobAsync(Uri url, CancellationToken cancellationToken)
    {
        RequireOwn(url);
        (HttpStatusCode status, byte[] body, _, _) = await SendAsync(url, null, cancellationToken).ConfigureAwait(false);
        ThrowUnlessSuccess(status, body);
        return body;
    }

    /// <summary>
    /// A blob's content: unpacked when it starts as gzip does, whichever headers it came with, and as it is otherwise;
    /// failing with <see cref="InvalidDataException"/> past <see cref="MostUnpackedBytes"/>.
    /// </summary>
    public static Stream Unpacked(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        MemoryStream read = new(blob, writable: false);
        return blob is [0x1F, 0x8B, ..]
            ? new LimitedStream(new GZipStream(read, CompressionMode.Decompress), MostUnpackedBytes)
            : new LimitedStream(read, MostUnpackedBytes);
    }

    public void Dispose() => _atOnce.Dispose();

    /// <summary>
    /// Whether <paramref name="url"/> is on Thunderstore's host or a host under it, such as its CDN, with its scheme.
    /// </summary>
    internal bool IsOwn(Uri url)
        => url.IsAbsoluteUri
            && url.Scheme == BaseAddress.Scheme
            && (string.Equals(url.Host, BaseAddress.Host, StringComparison.OrdinalIgnoreCase)
                || url.Host.EndsWith($".{BaseAddress.Host}", StringComparison.OrdinalIgnoreCase));

    private void RequireOwn(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!IsOwn(url))
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, $"Thunderstore pointed ModShelf to {url.Host}, which isn't Thunderstore's.");
        }
    }

    private async Task<(HttpStatusCode Status, byte[] Body, Uri? Location, DateTimeOffset? LastModified)> SendAsync(
        Uri url,
        DateTimeOffset? ifModifiedSince,
        CancellationToken cancellationToken)
    {
        await _atOnce.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HttpClient http = clients.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.IfModifiedSince = ifModifiedSince;
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, body, response.Headers.Location, response.Content.Headers.LastModified);
        }
        finally
        {
            _atOnce.Release();
        }
    }

    private static void ThrowUnlessSuccess(HttpStatusCode status, byte[] body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("Thunderstore asked ModShelf to slow down. Try again in a while.", null, status);
        }

        if ((int)status is < 200 or > 299)
        {
            throw new HttpRequestException($"Thunderstore answered {(int)status}. {Shorten(body)}".TrimEnd(), null, status);
        }
    }

    private static string Shorten(byte[] body)
    {
        string text = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, MaxMessageLength * 4)).Trim();
        return text.Length <= MaxMessageLength ? text : $"{text[..MaxMessageLength]}…";
    }
}
