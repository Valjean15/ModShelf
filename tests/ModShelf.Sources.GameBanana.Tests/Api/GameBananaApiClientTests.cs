using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests.Api;

/// <summary>
/// GameBanana's answers turned into results or the exceptions the app understands, whatever status or content type it
/// gives them with, a few requests at a time.
/// </summary>
public sealed class GameBananaApiClientTests : IDisposable
{
    private readonly RecordedGameBananaApi _api = new();

    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData(200, "text/html; charset=UTF-8")]
    [InlineData(404, "application/json; charset=UTF-8")]
    public async Task AnErrorInTheAnswerIsOneWhateverTheStatus(int status, string contentType)
    {
        _api.Answer("/apiv11/Mod/1/ProfilePage", answer => answer
            .WithStatusCode(status)
            .WithHeader("Content-Type", contentType)
            .WithBody("""{"_sErrorCode": "NO_SUCH_RECORD", "_sErrorMessage": "This Mod doesn't exist"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<GameBananaMod>("apiv11/Mod/1/ProfilePage"));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal("GameBanana refused: This Mod doesn't exist", refused.Message);
    }

    [Fact]
    public async Task AnInputErrorKeepsItsStatus()
    {
        _api.Answer("/apiv11/Mod/Index", answer => answer
            .WithStatusCode(HttpStatusCode.BadRequest)
            .WithBody("""{"_sErrorCode": "INPUT_ERRORS", "_aErrorData": {"_nPerpage": {"_sErrorCode": "INVALID_PERPAGE"}}}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<GameBananaMod>("apiv11/Mod/Index"));

        Assert.Equal((HttpStatusCode.BadRequest, "GameBanana refused: INPUT_ERRORS"), (refused.StatusCode, refused.Message));
    }

    [Fact]
    public async Task AnswersAsTextHtmlAreReadAsJson()
    {
        _api.Serve("game");

        GameBananaGame game = await GetAsync<GameBananaGame>("apiv11/Game/16951/ProfilePage");

        Assert.Equal((16951L, "Persona 5 Royal (PC)"), (game.Id, game.Name));
    }

    [Fact]
    public async Task BeingAskedToSlowDownIsTooManyRequestsAskedOnce()
    {
        _api.Answer("/apiv11/Mod/1/ProfilePage", answer => answer.WithStatusCode(HttpStatusCode.TooManyRequests).WithBody("<html>Slow down</html>"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<GameBananaMod>("apiv11/Mod/1/ProfilePage", retries: 3));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("GameBanana asked ModShelf to slow down. Try again in a while.", refused.Message);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AnAnswerThatIsntJsonIsAnInvalidResponse()
    {
        _api.Answer("/apiv11/Mod/1/ProfilePage", answer => answer.WithBody("<html>Maintenance</html>"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<GameBananaMod>("apiv11/Mod/1/ProfilePage"));

        Assert.Equal((HttpRequestError.InvalidResponse, (HttpStatusCode?)null), (refused.HttpRequestError, refused.StatusCode));
    }

    [Fact]
    public async Task AnotherRefusalSaysItsStatusAndAShortMessage()
    {
        _api.Answer("/apiv11/Mod/1/ProfilePage", answer => answer.WithStatusCode(HttpStatusCode.Forbidden).WithBody(new string('x', 1000)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync<GameBananaMod>("apiv11/Mod/1/ProfilePage"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.StartsWith("GameBanana answered 403. xxx", refused.Message, StringComparison.Ordinal);
        Assert.True(refused.Message.Length < 400);
    }

    [Fact]
    public async Task NamesTheAppAndAsksForJson()
    {
        _api.Serve("game");

        await GetAsync<GameBananaGame>("apiv11/Game/16951/ProfilePage");

        IDictionary<string, WireMock.Types.WireMockList<string>> headers = Assert.Single(_api.Requests).RequestMessage!.Headers!;
        Assert.StartsWith("ModShelf/", headers["User-Agent"].ToString(), StringComparison.Ordinal);
        Assert.Contains("application/json", headers["Accept"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsAFewRequestsAtOnceAtMost()
    {
        CountingHandler counting = new();
        ServiceCollection services = new();
        services.AddGameBananaSource(new GameBananaOptions { BaseAddress = new Uri("https://gamebanana.example/"), MaxRequestsAtOnce = 2 });
        services.AddHttpClient(GameBananaApiClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => counting);
        await using ServiceProvider provider = services.BuildServiceProvider();
        GameBananaApiClient client = provider.GetRequiredService<GameBananaApiClient>();

        await Task.WhenAll(Enumerable.Range(1, 6).Select(_ => client.GetAsync<GameBananaGame>("apiv11/Game/1/ProfilePage", TestContext.Current.CancellationToken)));

        Assert.Equal((6, 2), (counting.Sent, counting.MostAtOnce));
    }

    private async Task<T> GetAsync<T>(string path, int retries = 0)
    {
        await using ServiceProvider services = _api.BuildServices(retries: retries);
        return await services.GetRequiredService<GameBananaApiClient>().GetAsync<T>(path, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Answers each request with a game after a moment, counting how many it was sent at once.
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _now;
        private int _sent;
        private int _most;

        public int Sent => _sent;

        public int MostAtOnce => _most;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _now);
            Interlocked.Increment(ref _sent);
            int most;
            do
            {
                most = _most;
            }
            while (now > most && Interlocked.CompareExchange(ref _most, now, most) != most);

            await Task.Delay(50, cancellationToken);
            Interlocked.Decrement(ref _now);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"_idRow": 1, "_sName": "Game"}""") };
        }
    }
}
