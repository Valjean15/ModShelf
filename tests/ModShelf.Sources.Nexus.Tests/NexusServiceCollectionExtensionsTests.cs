using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// How a host plugs the source in.
/// </summary>
public sealed class NexusServiceCollectionExtensionsTests : IDisposable
{
    private readonly RecordedNexusApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public void RegistersTheSourceAsAModSource()
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddNexusSource();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<NexusSource>(Assert.Single(provider.GetServices<IModSource>()));
    }

    [Fact]
    public async Task CallingItTwiceChangesNothing()
    {
        // A server error that lasts, so the retries show too.
        _api.Answer("/v1/down.json", response => response.WithStatusCode(503));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICredentialProvider, NoLinkedAccount>();
        SourceHttpOptions http = new() { MaxRetryAttempts = 2, RetryDelay = TimeSpan.Zero };
        services.AddSourceHttpClients(http);
        NexusOptions options = new() { ApiBaseAddress = _api.BaseAddress };
        services.AddNexusSource(options);
        services.AddNexusSource(options);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetRequiredService<NexusApiClient>().GetAsync<object>("v1/down.json", TestContext.Current.CancellationToken));

        Assert.Single(provider.GetServices<IModSource>());
        Assert.Equal(1 + http.MaxRetryAttempts, _api.Requests.Count);
        Assert.All(_api.Requests, request => Assert.Equal("ModShelf", Assert.Single(request.RequestMessage!.Headers!["Application-Name"])));
    }

    [Fact]
    public async Task ItsClientHasNoOverallTimeLimitSinceEachAttemptHasOne()
    {
        await using ServiceProvider services = _api.BuildServices();

        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(NexusApiClient.ClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    public static TheoryData<NexusOptions> BrokenOptions() =>
    [
        new NexusOptions { ApiBaseAddress = new Uri("api/", UriKind.Relative) },
        new NexusOptions { ApiBaseAddress = new Uri("https://api.nexusmods.com/v1") },
        new NexusOptions { ApiBaseAddress = new Uri("ftp://api.nexusmods.com/") },
        new NexusOptions { UsersBaseAddress = new Uri("https://users.nexusmods.com/oauth") },
        new NexusOptions { OAuthClientId = " " },
    ];

    [Theory]
    [MemberData(nameof(BrokenOptions))]
    public void RefusesOptionsThatCantWork(NexusOptions options)
    {
        ServiceCollection services = new();

        Assert.ThrowsAny<ArgumentException>(() => services.AddNexusSource(options));
        Assert.Empty(services);
    }
}
