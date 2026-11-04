using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Sources;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

using Polly.Timeout;

namespace ModShelf.Infrastructure.Tests.Sources;

/// <summary>
/// The retries, time limits and refusal of redirects every source's API client gets from <c>AddSourceHttpClients()</c>,
/// whatever the site.
/// </summary>
public sealed class SourceHttpPipelineTests : IDisposable
{
    private const int Retries = 2;

    private static readonly SourceId Site = new("example");
    private static readonly string SiteClient = SourceHttpClients.NameFor(Site);
    private static readonly Uri Endpoint = new("https://api.example.org/v1/mods/7");

    private static readonly SourceHttpOptions Options = new()
    {
        MaxRetryAttempts = Retries,
        RetryDelay = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromMilliseconds(300),
    };

    private readonly ScriptedHttpHandler _site = new();
    private readonly ServiceProvider _services;

    public SourceHttpPipelineTests()
    {
        _services = Services(_site, Options);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task APassingFailureIsRetried(HttpStatusCode failure)
    {
        _site
            .Respond(() => new HttpResponseMessage(failure))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await ClientNamed(SiteClient).GetAsync(Endpoint, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _site.Requests.Count);
    }

    [Fact]
    public async Task ARetrySendsTheLinkedAccountAsTheFirstAttemptDid()
    {
        ScriptedCredentialProvider credentials = new();
        credentials.Current[Site] = new AccountCredentials("access-1");
        _site
            .Respond(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));
        await using ServiceProvider services = Services(_site, Options, credentials: credentials);

        using HttpResponseMessage response = await ClientNamed(SiteClient, services).GetAsync(Endpoint, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(_site.Requests, sent =>
        {
            Assert.Equal("Bearer access-1", sent.Authorization);
            Assert.Equal("example", sent.Headers![ScriptedCredentialProvider.AccountHeader]);
            Assert.Equal("?account=access-1", sent.Uri!.Query);
        });
    }

    [Fact]
    public async Task AFailureThatLastsIsReportedAfterTheRetries()
    {
        _site.RespondAlways(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using HttpResponseMessage response = await ClientNamed(SiteClient).GetAsync(Endpoint, Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1 + Retries, _site.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ARefusalOrTheRateLimitIsntRetried(HttpStatusCode refusal)
    {
        _site.RespondAlways(() => new HttpResponseMessage(refusal));

        using HttpResponseMessage response = await ClientNamed(SiteClient).GetAsync(Endpoint, Cancellation);

        Assert.Equal(refusal, response.StatusCode);
        Assert.Single(_site.Requests);
    }

    [Fact]
    public async Task AServersRetryAfterIsntWaitedFor()
    {
        _site
            .Respond(() =>
            {
                HttpResponseMessage maintenance = new(HttpStatusCode.ServiceUnavailable);
                maintenance.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return maintenance;
            })
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await ClientNamed(SiteClient).GetAsync(Endpoint, Cancellation)
            .WaitAsync(TimeSpan.FromSeconds(20), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RetriesWaitOnTheRegisteredClock()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        _site
            .Respond(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));
        await using ServiceProvider services = Services(_site, Options with { RetryDelay = TimeSpan.FromMinutes(1) }, clock);

        Task<HttpResponseMessage> sending = ClientNamed(SiteClient, services).GetAsync(Endpoint, Cancellation);
        bool firstSent = SpinWait.SpinUntil(() => _site.Requests.Count == 1, TimeSpan.FromSeconds(10));
        await Task.Delay(200, Cancellation);
        bool doneBeforeTheClockMoved = sending.IsCompleted;
        clock.Advance(TimeSpan.FromHours(1));
        using HttpResponseMessage response = await sending.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.True(firstSent);
        Assert.False(doneBeforeTheClockMoved);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _site.Requests.Count);
    }

    [Fact]
    public async Task AnAttemptWithoutAnAnswerIsRetriedThenReportedAsTheSiteNotAnswering()
    {
        NeverAnswers silent = new();
        await using ServiceProvider services = Services(silent, Options);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(
            () => ClientNamed(SiteClient, services).GetAsync(Endpoint, Cancellation).WaitAsync(TimeSpan.FromSeconds(20), Cancellation));

        Assert.Null(failed.StatusCode);
        Assert.IsType<TimeoutRejectedException>(Assert.IsType<TimeoutException>(failed.InnerException).InnerException);
        Assert.Equal(1 + Retries, silent.Requests);
    }

    [Fact]
    public async Task AnAnswerThatStallsPartWayIsRetriedThenReported()
    {
        // The headers arrive, then nothing more: a connection that dropped without saying so.
        _site.RespondAlways(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ScriptedStream(Encoding.UTF8.GetBytes("""{"mods": [1, 2, 3]}"""), 5, StreamEnding.Stall)),
        });

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(
            () => ClientNamed(SiteClient).GetAsync(Endpoint, Cancellation).WaitAsync(TimeSpan.FromSeconds(20), Cancellation));

        Assert.IsType<TimeoutException>(failed.InnerException);
        Assert.Equal(1 + Retries, _site.Requests.Count);
    }

    [Fact]
    public async Task AWholeAnswerIsReadBeforeTheCallerGetsIt()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"name": "Example"}""");
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ScriptedStream(body, body.Length, StreamEnding.End, chunkSize: 4)),
        });

        using HttpResponseMessage response = await ClientNamed(SiteClient).GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, Cancellation);

        // Buffered by the pipeline, so its length is known without reading it.
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public void RedirectsArentFollowedWhicheverHandlerTheHostChose()
    {
        SocketsHttpHandler sockets = new();
        HttpClientHandler classic = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICredentialProvider>(new ScriptedCredentialProvider());
        services.AddSourceHttpClients(Options);
        services.AddHttpClient(SourceHttpClients.NameFor(new SourceId("sockets"))).ConfigurePrimaryHttpMessageHandler(() => sockets);
        services.AddHttpClient(SourceHttpClients.NameFor(new SourceId("classic"))).ConfigurePrimaryHttpMessageHandler(() => classic);
        using ServiceProvider provider = services.BuildServiceProvider();

        using HttpClient first = ClientNamed(SourceHttpClients.NameFor(new SourceId("sockets")), provider);
        using HttpClient second = ClientNamed(SourceHttpClients.NameFor(new SourceId("classic")), provider);

        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(classic.AllowAutoRedirect);
    }

    [Fact]
    public async Task OtherClientsArentRetried()
    {
        _site.RespondAlways(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using HttpResponseMessage response = await ClientNamed("ModShelf.Other").GetAsync(Endpoint, Cancellation);

        Assert.Single(_site.Requests);
    }

    [Fact]
    public async Task CallingItAgainChangesNothing()
    {
        ScriptedHttpHandler site = new();
        site.RespondAlways(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICredentialProvider>(new ScriptedCredentialProvider());
        services.AddSourceHttpClients(Options);
        services.AddSourceHttpClients(Options with { MaxRetryAttempts = 5 });
        services.AddHttpClient(SiteClient).ConfigurePrimaryHttpMessageHandler(() => site);
        await using ServiceProvider provider = services.BuildServiceProvider();

        using HttpResponseMessage response = await ClientNamed(SiteClient, provider).GetAsync(Endpoint, Cancellation);

        Assert.Equal(1 + Retries, site.Requests.Count);
    }

    public static TheoryData<SourceHttpOptions> BrokenOptions() =>
    [
        new SourceHttpOptions { MaxRetryAttempts = -1 },
        new SourceHttpOptions { RetryDelay = TimeSpan.FromSeconds(-1) },
        new SourceHttpOptions { AttemptTimeout = TimeSpan.Zero },
    ];

    [Theory]
    [MemberData(nameof(BrokenOptions))]
    public void RefusesOptionsThatCantWork(SourceHttpOptions options)
    {
        ServiceCollection services = new();

        Assert.ThrowsAny<ArgumentException>(() => services.AddSourceHttpClients(options));
        Assert.Empty(services);
    }

    /// <summary>
    /// A source's client and another, both reaching <paramref name="network"/>, set up as the hosts set them up, on
    /// <paramref name="clock"/> and with <paramref name="credentials"/> when they're given.
    /// </summary>
    private static ServiceProvider Services(
        HttpMessageHandler network,
        SourceHttpOptions options,
        TimeProvider? clock = null,
        ScriptedCredentialProvider? credentials = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddSingleton<ICredentialProvider>(credentials ?? new ScriptedCredentialProvider());
        services.AddSourceHttpClients(options);
        services.AddHttpClient(SiteClient).ConfigurePrimaryHttpMessageHandler(() => network);
        services.AddHttpClient("ModShelf.Other").ConfigurePrimaryHttpMessageHandler(() => network);
        return services.BuildServiceProvider();
    }

    private static HttpClient ClientNamed(string name, IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>().CreateClient(name);

    private HttpClient ClientNamed(string name) => ClientNamed(name, _services);

    /// <summary>
    /// Takes every request and never answers, until the caller gives up.
    /// </summary>
    private sealed class NeverAnswers : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Never reached.");
        }
    }
}
