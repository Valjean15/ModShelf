using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Tests.Common;

using Polly.Timeout;

namespace ModShelf.Sources.Nexus.Tests.Api;

/// <summary>
/// The API client against the recorded API: what every request carries, and how answers become results or exceptions.
/// </summary>
public sealed partial class NexusApiClientTests : IDisposable
{
    private const string ModPath = "/v1/games/stardewvalley/mods/2400.json";
    private const string FlakyPath = "/v1/flaky.json";
    private const string GraphQlPath = "/v2/graphql";
    private const string TrackedPath = "/v1/user/tracked_mods.json";

    private readonly RecordedNexusApi _api = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task EveryRequestNamesTheAppAndItsVersion()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");

        await GetAsync<ModSummary>(ModPath);

        IDictionary<string, WireMock.Types.WireMockList<string>> headers = Assert.Single(_api.Requests).RequestMessage!.Headers!;
        Assert.Equal("ModShelf", Assert.Single(headers["Application-Name"]));
        Assert.Matches(Version(), Assert.Single(headers["Application-Version"]));
        Assert.StartsWith("ModShelf/", headers["User-Agent"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsTheApisSnakeCaseNames()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");
        string recordedName = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "mod")["response"]!["body"]!["name"]!.GetValue<string>();

        ModSummary mod = await GetAsync<ModSummary>(ModPath);

        Assert.Equal(2400, mod.ModId);
        Assert.Equal(recordedName, mod.Name);
    }

    [Fact]
    public async Task AModThatDoesntExistIsA404WithTheApisMessage()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod-not-found");
        string message = Message(RecordedNexusApi.Premium, "mod-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<ModSummary>("/v1/games/stardewvalley/mods/999999999.json"));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AFreeAccountsDownloadLinkRefusalReadsAsOneLine()
    {
        _api.Serve(RecordedNexusApi.Free, "download-link-without-key");
        string path = RecordedNexusApi.Recording(RecordedNexusApi.Free, "download-link-without-key")["request"]!["path"]!.GetValue<string>();

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<ModSummary>(path));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(Message(RecordedNexusApi.Free, "download-link-without-key"), refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedKeyIsAnAuthenticationFailure()
    {
        _api.Serve(RecordedNexusApi.Premium, "validate-invalid-key");

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetAsync<ModSummary>("/v1/users/validate.json"));

        Assert.Contains(Message(RecordedNexusApi.Premium, "validate-invalid-key"), refused.Message, StringComparison.Ordinal);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task ServerErrorsAreRetried()
    {
        _api.AnswerInTurn(
            FlakyPath,
            response => response.WithStatusCode(503),
            response => response.WithStatusCode(502),
            response => response.WithStatusCode(200).WithBody("""{"mod_id": 7, "name": "Fine"}"""));

        ModSummary mod = await GetAsync<ModSummary>(FlakyPath);

        Assert.Equal("Fine", mod.Name);
        Assert.Equal(3, _api.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsThatLastAreReportedAfterTheRetries()
    {
        _api.Answer(FlakyPath, response => response.WithStatusCode(503).WithBody("""{"message": "Down for maintenance"}"""));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<ModSummary>(FlakyPath, retries: 2));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Contains("Down for maintenance", failed.Message, StringComparison.Ordinal);
        Assert.Equal(3, _api.Requests.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task RefusalsAreNotRetried(int status)
    {
        _api.Answer(FlakyPath, response => response.WithStatusCode(status));

        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync<ModSummary>(FlakyPath));

        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AnAttemptThatTakesTooLongIsRetriedThenReported()
    {
        _api.Answer(FlakyPath, response => response.WithStatusCode(200).WithBody("{}").WithDelay(TimeSpan.FromSeconds(1)));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(
            () => GetAsync<ModSummary>(FlakyPath, retries: 1, attemptTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.IsType<TimeoutRejectedException>(failed.InnerException);
        Assert.Null(failed.StatusCode);

        // WireMock logs a request once it has answered it, after its delay.
        Stopwatch waited = Stopwatch.StartNew();
        while (_api.Requests.Count < 2 && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50, Cancellation);
        }

        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task ErrorMessagesNeverHoldTheRequestsAddress()
    {
        _api.Answer("/v1/secret.json", response => response.WithStatusCode(403).WithBody("""{"message": "No"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<ModSummary>("/v1/secret.json?key=nxm-key-123&expires=1"));

        Assert.DoesNotContain("nxm-key-123", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerItCantReadIsAFailureNotACrash()
    {
        _api.Answer(ModPath, response => response.WithStatusCode(200).WithBody("<html>Down for maintenance</html>"));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<ModSummary>(ModPath));

        Assert.IsType<System.Text.Json.JsonException>(failed.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task AGraphQlQueryIsPostedWithItsVariablesAndReadInCamelCase()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            """{"data": {"mod": {"modId": 2400, "name": "SMAPI"}}}"""));

        GraphQlData data = await QueryAsync<GraphQlData>("query Q($id: Int) { mod(id: $id) { modId name } }", new JsonObject { ["id"] = 2400 });

        Assert.Equal(new ModSummary(2400, "SMAPI"), data.Mod);
        JsonNode body = JsonNode.Parse(Assert.Single(_api.Requests).RequestMessage!.Body!)!;
        Assert.Equal("query Q($id: Int) { mod(id: $id) { modId name } }", body["query"]!.GetValue<string>());
        Assert.Equal(2400, body["variables"]!["id"]!.GetValue<int>());
        Assert.Equal("ModShelf", Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Headers!["Application-Name"]));
    }

    [Fact]
    public async Task GraphQlErrorsAreAFailureWithTheirMessages()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            """{"data": null, "errors": [{"message": "Field 'nope' doesn't exist"}, {"message": "Second\n   problem"}]}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => QueryAsync<GraphQlData>("{ nope }", []));

        Assert.Contains("Field 'nope' doesn't exist Second problem", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGraphQlAnswerWithoutDataIsAFailure()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody("""{"data": null}"""));

        await Assert.ThrowsAsync<HttpRequestException>(() => QueryAsync<GraphQlData>("{ mod { modId } }", []));
    }

    [Fact]
    public async Task AGraphQlQueryWithARefusedKeyIsAnAuthenticationFailure()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(401).WithBody("""{"message": "Please provide a valid API Key"}"""));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => QueryAsync<GraphQlData>("{ mod { modId } }", []));
    }

    [Theory]
    [InlineData(204, "")]
    [InlineData(200, "not JSON at all")]
    public async Task ARequestWhoseAnswerDoesntMatterSucceedsWhateverItSays(int status, string body)
    {
        _api.AnswerPost(TrackedPath, response => response.WithStatusCode(status).WithBody(body));

        await SendAsync(HttpMethod.Post, TrackedPath);

        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task ARequestWhoseAnswerDoesntMatterIsStillRefused()
    {
        _api.AnswerDelete(TrackedPath, response => response.WithStatusCode(403).WithBody("""{"message": "Not  your\n mod"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(HttpMethod.Delete, TrackedPath));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Nexus Mods answered 403. Not your mod", refused.Message);
    }

    [Fact]
    public async Task ARequestWhoseAnswerDoesntMatterWithARefusedKeyIsAnAuthenticationFailure()
    {
        _api.AnswerPost(TrackedPath, response => response.WithStatusCode(401).WithBody("""{"message": "Please provide a valid API Key"}"""));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => SendAsync(HttpMethod.Post, TrackedPath));
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex Version();

    /// <summary>
    /// The recorded error's text, with its runs of whitespace as one space, as the client reports it.
    /// </summary>
    private static string Message(string account, string name)
    {
        System.Text.Json.Nodes.JsonNode body = RecordedNexusApi.Recording(account, name)["response"]!["body"]!;
        string text = (body["message"] ?? body["error"])!.GetValue<string>();
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<T> GetAsync<T>(string path, int retries = 3, TimeSpan? attemptTimeout = null)
    {
        await using ServiceProvider services = _api.BuildServices(
            options => options with
            {
                MaxRetryAttempts = retries,
                AttemptTimeout = attemptTimeout ?? options.AttemptTimeout,
            },
            realClock: attemptTimeout is not null);
        return await services.GetRequiredService<NexusApiClient>().GetAsync<T>(path.TrimStart('/'), Cancellation);
    }

    private async Task<T> QueryAsync<T>(string query, JsonObject variables)
    {
        await using ServiceProvider services = _api.BuildServices();
        return await services.GetRequiredService<NexusApiClient>().QueryAsync<T>(query, variables, Cancellation);
    }

    private async Task SendAsync(HttpMethod method, string path)
    {
        await using ServiceProvider services = _api.BuildServices();
        await services.GetRequiredService<NexusApiClient>().SendAsync(new HttpRequestMessage(method, new Uri(path.TrimStart('/'), UriKind.Relative)), Cancellation);
    }

    private sealed record ModSummary(int ModId, string Name);

    private sealed record GraphQlData(ModSummary? Mod);
}
