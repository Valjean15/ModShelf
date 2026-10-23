using System.Net;
using System.Text;

using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Sources;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Infrastructure.Tests.Sources;

public sealed class LinkedAccountHandlerTests : IDisposable
{
    private static readonly SourceId Nexus = new("nexus");
    private static readonly Uri Endpoint = new("https://api.example.com/v1/games/skyrim/mods/12604.json?include=files");

    private readonly ScriptedHttpHandler _site = new();
    private readonly ScriptedCredentialProvider _credentials = new();
    private readonly HttpMessageInvoker _client;

    public LinkedAccountHandlerTests()
    {
        _client = new HttpMessageInvoker(new LinkedAccountHandler(Nexus, _credentials) { InnerHandler = _site });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task AddsTheLinkedAccountToTheRequest()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer access-1", Assert.Single(_site.Requests).Authorization);
    }

    [Fact]
    public async Task WithoutAnAccountTheRequestGoesOutAsBuilt()
    {
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        RecordedRequest sent = Assert.Single(_site.Requests);
        Assert.Null(sent.Authorization);
        Assert.Equal(Endpoint, sent.Uri);
    }

    [Fact]
    public async Task ARefusedRequestIsSentAgainWithRefreshedCredentials()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _credentials.Refresh = _ => Task.FromResult<AccountCredentials?>(new AccountCredentials("access-2"));
        _site
            .Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpRequestMessage request = NewRequest();
        request.Headers.Add("Application-Name", "ModShelf");

        using HttpResponseMessage response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("access-1", Assert.Single(_credentials.Refusals).Rejected.AccessToken);
        RecordedRequest retry = _site.Requests[1];
        Assert.Equal("Bearer access-2", retry.Authorization);
        Assert.Equal("nexus", retry.Headers![ScriptedCredentialProvider.AccountHeader]);
        Assert.Equal("ModShelf", retry.Headers["Application-Name"]);
        Assert.Equal(new Uri("https://api.example.com/v1/games/skyrim/mods/12604.json?include=files&account=access-2"), retry.Uri);
    }

    [Fact]
    public async Task TheBodyIsSentAgainWithTheRetry()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _credentials.Refresh = _ => Task.FromResult<AccountCredentials?>(new AccountCredentials("access-2"));
        _site
            .Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint)
        {
            // A stream that can be read only once, like a body produced on the fly.
            Content = new StreamContent(new OneWayStream(Encoding.UTF8.GetBytes("""{"endorse":true}"""))),
        };

        using HttpResponseMessage response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(_site.Requests, sent => Assert.Equal("""{"endorse":true}""", sent.Body));
    }

    [Fact]
    public async Task WhenTheRefreshFailsTheCallerGetsThe401()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(_site.Requests);
        Assert.Single(_credentials.Refusals);
    }

    [Fact]
    public async Task ARequestRefusedAgainAfterTheRefreshIsNotRetriedAgain()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _credentials.Refresh = _ => Task.FromResult<AccountCredentials?>(new AccountCredentials("access-2"));
        _site.RespondAlways(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, _site.Requests.Count);
        Assert.Single(_credentials.Refusals);
    }

    [Fact]
    public async Task A401WithoutAnAccountIsNotRefreshed()
    {
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_credentials.Refusals);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherRefusalsAreLeftToTheCaller(HttpStatusCode status)
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _site.Respond(() => new HttpResponseMessage(status));

        using HttpResponseMessage response = await SendAsync(NewRequest());

        Assert.Equal(status, response.StatusCode);
        Assert.Single(_site.Requests);
        Assert.Empty(_credentials.Refusals);
    }

    [Fact]
    public async Task ANetworkErrorWhileRefreshingReachesTheCaller()
    {
        _credentials.Current[Nexus] = new AccountCredentials("access-1");
        _credentials.Refresh = _ => throw new HttpRequestException("The site didn't answer.");
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(NewRequest()));
    }

    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, Endpoint);

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request, Cancellation);

    /// <summary>
    /// A stream that can't seek, so it can be read only once.
    /// </summary>
    private sealed class OneWayStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
