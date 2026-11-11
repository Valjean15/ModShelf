using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests.Api;

/// <summary>
/// mod.io's answers turned into results or the exceptions the app understands, and a request to slow down kept to: a
/// 429 leaves mod.io alone for as long as it asks, a minute when it doesn't say.
/// </summary>
public sealed class ModIoApiClientTests : IDisposable
{
    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public void Dispose() => _harness.Dispose();

    private Uri Mod => new(_harness.Api.ApiAddress, "games/6715/mods/4228735");

    private ModIoApiClient Client => _harness.Services.GetRequiredService<ModIoApiClient>();

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"error": {"code": 500, "error_ref": 10000, "message": "Something broke."}}""", "mod.io answered 500. Something broke.")]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "mod.io answered 502. <html>Bad gateway</html>")]
    [InlineData(HttpStatusCode.BadRequest, """{"error": {"code": 400, "message": ""}}""", """mod.io answered 400. {"error": {"code": 400, "message": ""}}""")]
    [InlineData(HttpStatusCode.Conflict, "", "mod.io answered 409.")]
    public async Task AnyOtherRefusalSaysWhatModIoSaid(HttpStatusCode status, string body, string message)
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer.WithStatusCode(status).WithBody(body));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Equal((status, message), (refused.StatusCode, refused.Message));
    }

    [Fact]
    public async Task ALongRefusalIsCutShort()
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer.WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody(new string('x', 1000)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Equal($"mod.io answered 503. {new string('x', 300)}…", refused.Message);
    }

    [Fact]
    public async Task NoCredentialsIsAnAccountToLink()
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer.WithStatusCode(HttpStatusCode.Unauthorized).WithBody("not json"));

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Equal("mod.io refused the API key or token linked, or none is linked.", refused.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task AnAnswerThatIsntAModIsInvalid(string body)
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer.WithBody(body));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Equal((HttpRequestError.InvalidResponse, (HttpStatusCode?)null), (refused.HttpRequestError, refused.StatusCode));
    }

    [Fact]
    public async Task ReadsNumbersWrittenAsText()
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer.WithBody("""{"id": "4228735", "game_id": "6715"}"""));

        ModIoMod mod = await Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation);

        Assert.Equal((4228735L, 6715L), (mod.Id, mod.GameId));
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("5", 5)]
    [InlineData("0", 60)]
    [InlineData(null, 60)]
    public async Task ASlowDownLeavesModIoAloneForAsLongAsItAsks(string? retryAfter, int seconds)
    {
        _harness.Api.Answer(Mod.AbsolutePath, answer =>
        {
            answer = answer.WithStatusCode(HttpStatusCode.TooManyRequests).WithBody("""{"error": {"code": 429, "error_ref": 11008, "message": "Too many requests."}}""");
            return retryAfter is null ? answer : answer.WithHeader("retry-after", retryAfter);
        });

        HttpRequestException first = await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));
        _harness.Api.Clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
        HttpRequestException held = await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));
        _harness.Api.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "mod.io asked ModShelf to slow down. Try again in a minute."),
            (first.StatusCode, first.Message));
        Assert.Equal((first.StatusCode, first.Message), (held.StatusCode, held.Message));
        Assert.Equal(2, _harness.Api.Requests.Count);
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains("mod.io asked ModShelf to slow down: it sends mod.io nothing until", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASlowDownAtATimeLeavesModIoAloneUntilThen()
    {
        DateTimeOffset now = _harness.Api.Clock.GetUtcNow();
        _harness.Api.Answer(Mod.AbsolutePath, answer => answer
            .WithStatusCode(HttpStatusCode.TooManyRequests)
            .WithHeader("Date", now.ToString("R"))
            .WithHeader("Retry-After", now.AddSeconds(90).ToString("R")));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));
        _harness.Api.Clock.Advance(TimeSpan.FromSeconds(89));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client.GetAsync<ModIoMod>(Mod, ModIoHarness.Cancellation));

        Assert.Single(_harness.Api.Requests);
    }

    [Fact]
    public async Task ASlowDownIsNeverRetried()
    {
        using RecordedModIoApi api = new();
        api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithStatusCode(HttpStatusCode.TooManyRequests));
        using ServiceProvider services = api.BuildServices(retries: 3, linked: RecordedModIoApi.AccessToken);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => services.GetRequiredService<ModIoApiClient>().GetAsync<ModIoMod>(new Uri(api.ApiAddress, "games/6715/mods/4228735"), ModIoHarness.Cancellation));

        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task ARequestWaitsItsTurnWithoutRunningOutOfTime()
    {
        using RecordedModIoApi api = new();
        api.ServeAll();
        using ServiceProvider services = api.BuildServices(retries: 0, linked: RecordedModIoApi.AccessToken, requestsPerMinute: 2);
        ModIoApiClient client = services.GetRequiredService<ModIoApiClient>();
        Uri mod = new(api.ApiAddress, "games/6715/mods/4228735");

        await client.GetAsync<ModIoMod>(mod, ModIoHarness.Cancellation);
        await client.GetAsync<ModIoMod>(mod, ModIoHarness.Cancellation);
        Task<ModIoMod> third = client.GetAsync<ModIoMod>(mod, ModIoHarness.Cancellation);
        await Task.Delay(100, ModIoHarness.Cancellation);
        int beforeAMinute = api.Requests.Count;

        // Each attempt has 10 seconds, which a minute's wait would run past were it one of them.
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await third;

        Assert.Equal((2, 3), (beforeAMinute, api.Requests.Count));
    }

    [Fact]
    public async Task NothingWaitsItsTurnWhileModIoIsLeftAlone()
    {
        using RecordedModIoApi api = new();
        api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithStatusCode(HttpStatusCode.TooManyRequests));
        using ServiceProvider services = api.BuildServices(retries: 0, linked: RecordedModIoApi.AccessToken, requestsPerMinute: 1);
        ModIoApiClient client = services.GetRequiredService<ModIoApiClient>();
        Uri mod = new(api.ApiAddress, "games/6715/mods/4228735");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync<ModIoMod>(mod, ModIoHarness.Cancellation));
        HttpRequestException held = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync<ModIoMod>(mod, ModIoHarness.Cancellation));

        Assert.Equal(HttpStatusCode.TooManyRequests, held.StatusCode);
        Assert.Single(api.Requests);
    }
}
