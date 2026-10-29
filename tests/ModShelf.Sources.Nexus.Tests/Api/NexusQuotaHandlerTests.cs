using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Tests.Common;

using WireMock.ResponseBuilders;

namespace ModShelf.Sources.Nexus.Tests.Api;

/// <summary>
/// The rate limit: read from every response, and requests held back while it's used up.
/// </summary>
public sealed class NexusQuotaHandlerTests : IAsyncDisposable
{
    private const string Path = "/v1/games/stardewvalley/mods/2400.json";

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusQuotaHandlerTests()
    {
        _services = _api.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusApiClient Client => _services.GetRequiredService<NexusApiClient>();

    private NexusQuota Quota => _services.GetRequiredService<NexusQuota>();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _api.Dispose();
    }

    [Fact]
    public async Task TheRateLimitIsReadFromEveryResponse()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");
        System.Text.Json.Nodes.JsonNode headers = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "mod")["response"]!["headers"]!;

        await GetAsync();

        NexusRateLimit limit = Assert.IsType<NexusRateLimit>(Quota.Current);
        Assert.Equal(Number(headers, "X-RL-Hourly-Limit"), limit.HourlyLimit);
        Assert.Equal(Number(headers, "X-RL-Hourly-Remaining"), limit.HourlyRemaining);
        Assert.Equal(Time(headers, "X-RL-Hourly-Reset"), limit.HourlyReset);
        Assert.Equal(Number(headers, "X-RL-Daily-Limit"), limit.DailyLimit);
        Assert.Equal(Number(headers, "X-RL-Daily-Remaining"), limit.DailyRemaining);
        Assert.Equal(Time(headers, "X-RL-Daily-Reset"), limit.DailyReset);
    }

    [Fact]
    public async Task RequestsAreHeldBackWhileBothAllowancesAreUsedUp()
    {
        DateTimeOffset hourlyReset = _api.Time.GetUtcNow().AddMinutes(20);
        _api.Answer(Path, response => WithLimit(response.WithStatusCode(200).WithBody("{}"), hourly: 0, daily: 0, hourlyReset));
        await GetAsync();

        HttpRequestException heldBack = await Assert.ThrowsAsync<HttpRequestException>(GetAsync);

        Assert.Equal(HttpStatusCode.TooManyRequests, heldBack.StatusCode);
        Assert.Contains(hourlyReset.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), heldBack.Message, StringComparison.Ordinal);
        Assert.Single(_api.Requests);
        Assert.Contains(_api.Logs.Entries, entry => entry.Contains("Held back a Nexus Mods request", StringComparison.Ordinal));

        // Once the hourly allowance resets, requests go out again.
        _api.Time.Advance(TimeSpan.FromMinutes(21));
        await GetAsync();
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task ARetryIsHeldBackWhenTheFailedAttemptUsedUpTheLimit()
    {
        _api.Answer(Path, response => WithLimit(response.WithStatusCode(503), hourly: 0, daily: 0, _api.Time.GetUtcNow().AddMinutes(20)));

        HttpRequestException heldBack = await Assert.ThrowsAsync<HttpRequestException>(GetAsync);

        Assert.Equal(HttpStatusCode.TooManyRequests, heldBack.StatusCode);
        Assert.Single(_api.Requests);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public async Task EitherAllowanceLetsRequestsThrough(int hourly, int daily)
    {
        _api.Answer(Path, response => WithLimit(response.WithStatusCode(200).WithBody("{}"), hourly, daily, _api.Time.GetUtcNow().AddMinutes(20)));

        await GetAsync();
        await GetAsync();

        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task ARefusalForTheRateLimitHoldsRequestsBackAsLongAsTheApiAsks()
    {
        _api.Answer(Path, response => response.WithStatusCode(429).WithHeader("Retry-After", "120"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Single(_api.Requests);
        Assert.Contains(_api.Logs.Entries, entry => entry.Contains("refused a request for its rate limit", StringComparison.Ordinal));

        _api.Time.Advance(TimeSpan.FromSeconds(121));
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task ARefusalWithoutRetryAfterHoldsRequestsBackForAnHour()
    {
        _api.Answer(Path, response => response.WithStatusCode(429));
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);

        _api.Time.Advance(TimeSpan.FromMinutes(59));
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        Assert.Single(_api.Requests);

        _api.Time.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task AHoldBackLastsAnHourAtMostWhateverTheResetTimesSay()
    {
        // Reset times far ahead, as a clock running hours behind the API's reads them: the hourly allowance resets
        // within the hour whatever they say.
        _api.Answer(Path, response => WithLimit(response.WithStatusCode(200).WithBody("{}"), hourly: 0, daily: 0, _api.Time.GetUtcNow().AddHours(5)));
        await GetAsync();
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);

        _api.Time.Advance(TimeSpan.FromMinutes(61));
        await GetAsync();

        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task ARefusalWhoseHeadersShowAllowanceLeftHoldsBackBriefly()
    {
        // Refused by something that limits bursts rather than by the account's allowances, which have some left.
        _api.AnswerInTurn(
            Path,
            response => WithLimit(response.WithStatusCode(429), hourly: 10, daily: 0, _api.Time.GetUtcNow().AddMinutes(20)),
            response => WithLimit(response.WithStatusCode(200).WithBody("{}"), hourly: 9, daily: 0, _api.Time.GetUtcNow().AddMinutes(20)));
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        await Assert.ThrowsAsync<HttpRequestException>(GetAsync);
        Assert.Single(_api.Requests);

        _api.Time.Advance(TimeSpan.FromMinutes(2));
        await GetAsync();

        Assert.Equal(2, _api.Requests.Count);
        Assert.Equal(9, Quota.Current!.HourlyRemaining);
    }

    [Fact]
    public async Task ResponsesWithoutTheHeadersKeepTheLastKnownLimit()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");
        await GetAsync();
        NexusRateLimit? known = Quota.Current;
        _api.Answer("/v1/plain.json", response => response.WithStatusCode(200).WithBody("{}"));

        await Client.GetAsync<object>("v1/plain.json", Cancellation);

        Assert.Equal(known, Quota.Current);
    }

    private static int Number(System.Text.Json.Nodes.JsonNode headers, string name)
        => int.Parse(Header(headers, name), CultureInfo.InvariantCulture);

    private static DateTimeOffset Time(System.Text.Json.Nodes.JsonNode headers, string name)
        => DateTimeOffset.Parse(Header(headers, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    // Header names ignore case; the API sends these in lowercase.
    private static string Header(System.Text.Json.Nodes.JsonNode headers, string name)
        => headers.AsObject().Single(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value!.GetValue<string>();

    private static IResponseBuilder WithLimit(IResponseBuilder response, int hourly, int daily, DateTimeOffset hourlyReset)
        => response
            .WithHeader("X-RL-Hourly-Limit", "500")
            .WithHeader("X-RL-Hourly-Remaining", hourly.ToString(CultureInfo.InvariantCulture))
            .WithHeader("X-RL-Hourly-Reset", hourlyReset.ToString("O", CultureInfo.InvariantCulture))
            .WithHeader("X-RL-Daily-Limit", "20000")
            .WithHeader("X-RL-Daily-Remaining", daily.ToString(CultureInfo.InvariantCulture))
            .WithHeader("X-RL-Daily-Reset", hourlyReset.AddHours(10).ToString("O", CultureInfo.InvariantCulture));

    private Task GetAsync() => Client.GetAsync<object>(Path.TrimStart('/'), Cancellation);
}
