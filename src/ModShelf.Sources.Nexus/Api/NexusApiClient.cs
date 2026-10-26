using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

using Polly.Timeout;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// Calls the Nexus Mods API through the source's HTTP client, and turns its answers into results or exceptions the
/// rest of the app understands.
/// </summary>
/// <remarks>
/// Exception messages never hold a request's URL: a download link request carries the <c>nxm://</c> key in its query.
/// </remarks>
internal sealed partial class NexusApiClient(IHttpClientFactory clients)
{
    // Error bodies can be whole HTML pages; only this much of a message is kept.
    private const int MaxMessageLength = 300;

    /// <summary>
    /// The API's JSON: snake_case names, such as <c>user_id</c>.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// The v2 GraphQL API's JSON: camelCase names, such as <c>modId</c>.
    /// </summary>
    internal static readonly JsonSerializerOptions GraphQlJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The name of the source's client, which the app sets up to add the linked account.
    /// </summary>
    internal static string ClientName { get; } = SourceHttpClients.NameFor(NexusSource.NexusId);

    /// <summary>
    /// Sends a GET request to <paramref name="relativeUrl"/>, such as <c>v1/users/validate.json</c>, and reads the
    /// answer.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">The API refused the account's key or token (401).</exception>
    /// <exception cref="HttpRequestException">
    /// Any other refusal or failure. <see cref="HttpRequestException.StatusCode"/> says which: 429 when the rate limit is
    /// used up, null when Nexus Mods couldn't be reached or didn't answer in time.
    /// </exception>
    public Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        return SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, new Uri(relativeUrl, UriKind.Relative)), cancellationToken);
    }

    /// <summary>
    /// Sends <paramref name="request"/>, disposing it afterwards, and reads the answer.
    /// </summary>
    /// <inheritdoc cref="GetAsync" path="/exception"/>
    public Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync<T>(request, Json, cancellationToken);

    /// <summary>
    /// Runs a query against the v2 GraphQL API, <c>v2/graphql</c>, and reads the data it answers.
    /// </summary>
    /// <param name="query">The GraphQL document, such as one of the source's <c>.graphql</c> files.</param>
    /// <param name="variables">The query's variables, with the API's camelCase names.</param>
    /// <exception cref="AuthenticationFailedException">The API refused the account's key or token (401).</exception>
    /// <exception cref="HttpRequestException">
    /// Any other refusal or failure, including an answer that lists GraphQL errors instead of data.
    /// </exception>
    public async Task<T> QueryAsync<T>(string query, JsonObject variables, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(variables);

        HttpRequestMessage request = new(HttpMethod.Post, new Uri("v2/graphql", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["query"] = query, ["variables"] = variables.DeepClone() }, options: GraphQlJson),
        };
        NexusGraphQlAnswer<T> answer = await SendAsync<NexusGraphQlAnswer<T>>(request, GraphQlJson, cancellationToken).ConfigureAwait(false);
        if (answer.Errors is { Count: > 0 } errors)
        {
            string messages = string.Join(" ", errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)));
            throw new HttpRequestException($"Nexus Mods refused the query. {Shorten(messages)}");
        }

        return answer.Data ?? throw new HttpRequestException("Nexus Mods answered the query with no data.");
    }

    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = await ReadMessageAsync(response, cancellationToken).ConfigureAwait(false);
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new AuthenticationFailedException($"Nexus Mods refused the account. {message}"),
            HttpStatusCode.TooManyRequests => new HttpRequestException(
                $"The Nexus Mods API's rate limit for this account is used up{Until(response.Headers.RetryAfter?.Date)}.",
                null,
                HttpStatusCode.TooManyRequests),
            _ => new HttpRequestException($"Nexus Mods answered {(int)response.StatusCode}. {message}", null, response.StatusCode),
        };
    }

    /// <summary>
    /// What an error body says, in its <c>message</c> or <c>error</c>, such as <c>{"code":403,"message":"…"}</c> or
    /// <c>{"error":"Mod ID 1 not found for stardewvalley"}</c>; otherwise the reason phrase. Runs of whitespace, which
    /// some messages have in the middle, become one space.
    /// </summary>
    private static async Task<string> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? message = null;
        try
        {
            NexusError? error = await response.Content.ReadFromJsonAsync<NexusError>(Json, cancellationToken).ConfigureAwait(false);
            message = string.IsNullOrWhiteSpace(error?.Message) ? error?.Error : error.Message;
        }
        catch (JsonException)
        {
            // Not a JSON error body.
        }

        return Shorten(string.IsNullOrWhiteSpace(message) ? response.ReasonPhrase ?? string.Empty : message);
    }

    /// <summary>
    /// The message on one line, with runs of whitespace as one space, cut at <see cref="MaxMessageLength"/> characters.
    /// </summary>
    private static string Shorten(string message)
    {
        string line = Whitespace().Replace(message, " ").Trim();
        return line.Length > MaxMessageLength ? line[..MaxMessageLength] + "…" : line;
    }

    private static string Until(DateTimeOffset? date)
        => date is { } until ? string.Create(CultureInfo.InvariantCulture, $" until {until.ToLocalTime():yyyy-MM-dd HH:mm}") : string.Empty;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonSerializerOptions json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using (request)
        {
            HttpResponseMessage response;
            try
            {
                response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutRejectedException exception)
            {
                throw new HttpRequestException("Nexus Mods didn't answer in time.", exception);
            }

            using (response)
            {
                await ThrowIfRefusedAsync(response, cancellationToken).ConfigureAwait(false);
                try
                {
                    return await response.Content.ReadFromJsonAsync<T>(json, cancellationToken).ConfigureAwait(false)
                        ?? throw new HttpRequestException("Nexus Mods answered with nothing.", null, response.StatusCode);
                }
                catch (JsonException exception)
                {
                    // The API changed, or answered with something other than JSON: a failure to report, not a crash.
                    throw new HttpRequestException("Nexus Mods answered in a way ModShelf doesn't understand.", exception, response.StatusCode);
                }
            }
        }
    }

    private sealed record NexusError(string? Message, string? Error);
}
