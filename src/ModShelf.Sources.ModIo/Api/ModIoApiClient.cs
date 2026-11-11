using System.Net;
using System.Text.Json;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Calls mod.io's REST API through the source's HTTP client, each request in its turn (<see cref="ModIoPacer"/>), and
/// turns its answers into results or exceptions the rest of the app understands. The client's handlers leave mod.io
/// alone after it asks to slow down (<see cref="ModIoSlowDownHandler"/>) and keep requests for an access token from going
/// out with an API key (<see cref="ModIoTokenGate"/>).
/// </summary>
/// <remarks>
/// mod.io answers every refusal with <c>{"error": {"code", "error_ref", "message"}}</c>: 401 for credentials it refuses
/// or none, 403 for a game closed to apps or hidden (14007), 404 for a game, mod or file that doesn't exist (14000), and
/// 429 once the requests of the minute are used up. Its messages never quote the credentials, and nor do these.
/// </remarks>
internal sealed class ModIoApiClient(IHttpClientFactory clients, ModIoPacer pacer)
{
    /// <summary>
    /// The header that asks for a platform's files, such as <c>windows</c>, of a game with files per platform.
    /// </summary>
    public const string PlatformHeader = "X-Modio-Platform";

    // Error answers can be whole HTML pages; only this much of a message is kept.
    private const int MaxMessageLength = 300;

    /// <summary>
    /// The API's JSON: names such as <c>name_id</c>, given on each property; numbers sometimes written as text, which the
    /// web's defaults read.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The name of the source's client, which the app sets up with the linked account, its retries and time limits.
    /// </summary>
    internal static string ClientName { get; } = SourceHttpClients.NameFor(ModIoSource.ModIoId);

    /// <summary>
    /// Sends a GET request to <paramref name="address"/>, as the platform's when one is given, and reads the answer.
    /// </summary>
    /// <param name="tokenOnly">
    /// For a request mod.io answers for an access token only, as subscriptions: what to throw when an API key is linked,
    /// as the request is then never sent. Null for a request any credential may send.
    /// </param>
    /// <exception cref="AuthenticationFailedException">mod.io refused the credentials linked, or none is.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io refused or failed. <see cref="HttpRequestException.StatusCode"/> says which: 403 for a game closed to
    /// apps, 404 for a game, mod or file that doesn't exist, 429 when mod.io asks to slow down; null when it couldn't be
    /// reached, didn't answer in time, or answered with something other than JSON.
    /// </exception>
    public async Task<T> GetAsync<T>(Uri address, string? platform, Func<Exception>? tokenOnly, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(address, platform, tokenOnly is not null);
        (HttpStatusCode status, string text, _) = await ExchangeAsync(request, tokenOnly, cancellationToken).ConfigureAwait(false);
        return Read<T>(status, text);
    }

    /// <summary>
    /// Sends a GET request to <paramref name="address"/>, for any credential, and reads the answer.
    /// </summary>
    /// <inheritdoc cref="GetAsync{T}(Uri, string?, Func{Exception}?, CancellationToken)" path="/exception"/>
    public Task<T> GetAsync<T>(Uri address, CancellationToken cancellationToken)
        => GetAsync<T>(address, platform: null, tokenOnly: null, cancellationToken);

    /// <summary>
    /// Sends <paramref name="request"/>, as one that carries the credentials it checks itself, and reads the answer.
    /// </summary>
    /// <inheritdoc cref="GetAsync{T}(Uri, string?, Func{Exception}?, CancellationToken)" path="/exception"/>
    public async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using (request)
        {
            (HttpStatusCode status, string text, _) = await ExchangeAsync(request, tokenOnly: null, cancellationToken).ConfigureAwait(false);
            return Read<T>(status, text);
        }
    }

    /// <summary>
    /// Where a link on mod.io's API sends its download on to, such as its file server: asked of mod.io with the account
    /// linked, without following it.
    /// </summary>
    /// <param name="tokenOnly">
    /// For a game that gives its files to an access token only: what to throw when an API key is linked.
    /// </param>
    /// <inheritdoc cref="GetAsync{T}(Uri, string?, Func{Exception}?, CancellationToken)" path="/exception"/>
    public async Task<Uri> GetRedirectAsync(Uri address, Func<Exception>? tokenOnly, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(address, platform: null, tokenOnly is not null);
        (HttpStatusCode status, string text, Uri? location) = await ExchangeAsync(request, tokenOnly, cancellationToken).ConfigureAwait(false);
        if ((int)status is >= 300 and <= 399 && location is not null)
        {
            return location.IsAbsoluteUri ? location : new Uri(address, location);
        }

        throw (int)status is >= 200 and <= 399
            ? new HttpRequestException(HttpRequestError.InvalidResponse, "mod.io didn't say where to download the file from.")
            : Refusal(status, text);
    }

    private static HttpRequestMessage Request(Uri address, string? platform, bool needsToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        HttpRequestMessage request = new(HttpMethod.Get, address);
        if (platform is not null)
        {
            request.Headers.Add(PlatformHeader, platform);
        }

        if (needsToken)
        {
            request.Options.Set(ModIoRequests.NeedsToken, true);
        }

        return request;
    }

    /// <summary>
    /// The answer, read as <typeparamref name="T"/>, or the refusal it is.
    /// </summary>
    private static T Read<T>(HttpStatusCode status, string text)
    {
        if ((int)status is < 200 or > 299)
        {
            throw Refusal(status, text);
        }

        T? answer;
        try
        {
            answer = JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException invalid)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "mod.io answered with something other than JSON.", invalid);
        }

        return answer ?? throw new HttpRequestException(HttpRequestError.InvalidResponse, "mod.io answered with nothing.");
    }

    /// <summary>
    /// What a refusal means, from its status and mod.io's answer.
    /// </summary>
    private static Exception Refusal(HttpStatusCode status, string text)
    {
        string? message = Message(text);
        return status switch
        {
            HttpStatusCode.Unauthorized => new AuthenticationFailedException(
                $"mod.io refused the API key or token linked, or none is linked{(message is null ? "." : $": {message}")}"),
            HttpStatusCode.Forbidden => new HttpRequestException(
                "mod.io doesn't let apps such as ModShelf see this game: it's closed to them, or hidden.", null, status),
            HttpStatusCode.NotFound => new HttpRequestException("mod.io has no such game, mod or file.", null, status),
            HttpStatusCode.TooManyRequests => new HttpRequestException(
                "mod.io asked ModShelf to slow down. Try again in a minute.", null, status),
            _ => new HttpRequestException($"mod.io answered {(int)status}. {message ?? Shorten(text)}".TrimEnd(), null, status),
        };
    }

    /// <summary>
    /// The message in mod.io's refusal, or null when the answer has none.
    /// </summary>
    private static string? Message(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ModIoErrorAnswer>(text, Json)?.Error?.Message is { Length: > 0 } message ? Shorten(message) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length <= MaxMessageLength ? trimmed : string.Concat(trimmed.AsSpan(0, MaxMessageLength), "…");
    }

    private async Task<(HttpStatusCode Status, string Text, Uri? Location)> ExchangeAsync(
        HttpRequestMessage request,
        Func<Exception>? tokenOnly,
        CancellationToken cancellationToken)
    {
        request.Headers.Accept.ParseAdd("application/json");

        // Its turn is waited for before the client's time limit on each attempt starts; nothing waits while mod.io is
        // left alone.
        if (pacer.HeldUntil is null)
        {
            await pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        HttpClient http = clients.CreateClient(ClientName);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (tokenOnly is not null && request.Options.TryGetValue(ModIoRequests.HeldForToken, out bool held) && held)
        {
            throw tokenOnly();
        }

        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, text, response.Headers.Location);
    }
}
