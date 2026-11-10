using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// Calls GameBanana's API, <c>apiv11</c>, through the source's HTTP client, a few requests at a time, and turns its
/// answers into results or exceptions the rest of the app understands.
/// </summary>
/// <remarks>
/// GameBanana answers a game or mod that doesn't exist with 404 at first, then, once it keeps the answer, with 200 and the
/// same error in the body, and serves some answers as <c>text/html</c>; so the body is read whatever the status or the
/// content type, and an error in it is one whatever the status.
/// </remarks>
internal sealed class GameBananaApiClient(IHttpClientFactory clients, int maxRequestsAtOnce) : IDisposable
{
    // Error bodies can be whole HTML pages; only this much of a message is kept.
    private const int MaxMessageLength = 300;

    private const string NoSuchRecord = "NO_SUCH_RECORD";

    /// <summary>
    /// The API's JSON: names such as <c>_idRow</c>, given on each property; numbers sometimes written as text.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly SemaphoreSlim _atOnce = new(maxRequestsAtOnce, maxRequestsAtOnce);

    /// <summary>
    /// The name of the source's client, which the app sets up with its retries and time limits.
    /// </summary>
    internal static string ClientName { get; } = SourceHttpClients.NameFor(GameBananaSource.GameBananaId);

    /// <summary>
    /// Sends a GET request to <paramref name="relativeUrl"/>, such as <c>apiv11/Mod/440088/ProfilePage</c>, and reads the
    /// answer.
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// GameBanana refused or failed. <see cref="HttpRequestException.StatusCode"/> says which: 404 when the game or mod
    /// doesn't exist, whatever status GameBanana gave; 429 when it asks to slow down; null when it couldn't be reached,
    /// didn't answer in time, or answered with something other than JSON.
    /// </exception>
    public async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        string text;
        HttpStatusCode status;
        await _atOnce.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HttpClient http = clients.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(relativeUrl, UriKind.Relative));
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            status = response.StatusCode;
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _atOnce.Release();
        }

        JsonNode? answer = Parse(text);
        if (answer is JsonObject body && body["_sErrorCode"]?.GetValueKind() == JsonValueKind.String)
        {
            throw Refusal(body, status);
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GameBanana asked ModShelf to slow down. Try again in a while.", null, status);
        }

        if ((int)status is < 200 or > 299)
        {
            throw new HttpRequestException($"GameBanana answered {(int)status}. {Shorten(text)}", null, status);
        }

        if (answer is null)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "GameBanana answered with something other than JSON.");
        }

        return answer.Deserialize<T>(Json)
            ?? throw new HttpRequestException(HttpRequestError.InvalidResponse, "GameBanana answered with nothing.");
    }

    public void Dispose() => _atOnce.Dispose();

    /// <summary>
    /// The answer as JSON, or null when it isn't.
    /// </summary>
    private static JsonNode? Parse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What an error GameBanana gave in its answer means: 404 for something that doesn't exist, whatever the status.
    /// </summary>
    private static HttpRequestException Refusal(JsonObject body, HttpStatusCode status)
    {
        string code = body["_sErrorCode"]!.GetValue<string>();
        string? message = body["_sErrorMessage"]?.GetValueKind() == JsonValueKind.String ? body["_sErrorMessage"]!.GetValue<string>() : null;
        HttpStatusCode meant = code == NoSuchRecord ? HttpStatusCode.NotFound
            : (int)status is >= 400 and <= 599 ? status
            : HttpStatusCode.BadRequest;
        return new HttpRequestException($"GameBanana refused: {Shorten(message ?? code)}", null, meant);
    }

    private static string Shorten(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length <= MaxMessageLength ? trimmed : string.Concat(trimmed.AsSpan(0, MaxMessageLength), "…");
    }
}
