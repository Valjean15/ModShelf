using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// Asks CurseForge's Core API through the client the app sets up for the source, which adds the linked API key to each
/// request (ADR 0107).
/// </summary>
internal sealed class CurseForgeApiClient(IHttpClientFactory clients, Uri apiAddress)
{
    // Error answers can be whole HTML pages; only this much of one is kept.
    private const int MaxMessageLength = 300;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string ClientName { get; } = SourceHttpClients.NameFor(CurseForgeSource.CurseForgeId);

    /// <summary>
    /// An address on the API, such as <c>v1/games</c>.
    /// </summary>
    public Uri Address(string relative) => new(apiAddress, relative);

    /// <returns>The answer's data and, for a list, its page.</returns>
    /// <exception cref="AuthenticationFailedException">CurseForge refused the API key, or none is linked.</exception>
    /// <exception cref="HttpRequestException">CurseForge couldn't be reached, or refused for another reason.</exception>
    public Task<CurseForgeAnswer<T>> GetAsync<T>(string relative, CancellationToken cancellationToken)
        => SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, Address(relative)), cancellationToken);

    public Task<CurseForgeAnswer<T>> PostAsync<T>(string relative, object body, CancellationToken cancellationToken)
        => SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, Address(relative)) { Content = JsonContent.Create(body, options: Json) }, cancellationToken);

    /// <summary>
    /// Sends a request made elsewhere, such as one the account's strategy authorized itself while linking.
    /// </summary>
    public async Task<CurseForgeAnswer<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using (request)
        {
            request.Headers.Accept.ParseAdd("application/json");
            HttpClient http = clients.CreateClient(ClientName);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Refusal(response.StatusCode, text);
            }

            try
            {
                return JsonSerializer.Deserialize<CurseForgeAnswer<T>>(text, Json)
                    ?? throw new HttpRequestException(HttpRequestError.InvalidResponse, "CurseForge answered with nothing.");
            }
            catch (JsonException invalid)
            {
                throw new HttpRequestException(HttpRequestError.InvalidResponse, "CurseForge answered with something other than JSON.", invalid);
            }
        }
    }

    /// <summary>
    /// CurseForge answers 403, with nothing in it, to a request without a key it takes: it never says 401.
    /// </summary>
    private static Exception Refusal(HttpStatusCode status, string text) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AuthenticationFailedException(
            "CurseForge refused the API key linked, or none is linked. Make one on the CurseForge for Studios console, and link it."),
        HttpStatusCode.NotFound => new HttpRequestException("CurseForge has no such game, mod or file.", null, status),
        HttpStatusCode.TooManyRequests => new HttpRequestException("CurseForge asked ModShelf to slow down. Try again in a minute.", null, status),
        _ => new HttpRequestException($"CurseForge answered {(int)status}. {Shorten(text)}".TrimEnd(), null, status),
    };

    private static string Shorten(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length <= MaxMessageLength ? trimmed : string.Concat(trimmed.AsSpan(0, MaxMessageLength), "…");
    }
}
