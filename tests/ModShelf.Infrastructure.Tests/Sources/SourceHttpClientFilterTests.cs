using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Infrastructure.Tests.Sources;

/// <summary>
/// Source API clients as the hosts register them, through <c>AddSourceHttpClients()</c> and the real client factory.
/// </summary>
public sealed class SourceHttpClientFilterTests : IDisposable
{
    private static readonly SourceId Nexus = new("nexus");
    private static readonly Uri Endpoint = new("https://api.example.com/v1/users/validate.json");

    private readonly ScriptedHttpHandler _site = new();
    private readonly ScriptedCredentialProvider _credentials = new();
    private readonly ServiceProvider _services;

    public SourceHttpClientFilterTests()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICredentialProvider>(_credentials);
        services.AddSourceHttpClients();
        services.AddSourceHttpClients();

        // The network, for this test: both clients reach the scripted site.
        services.AddHttpClient(SourceHttpClients.NameFor(Nexus)).ConfigurePrimaryHttpMessageHandler(() => _site);
        services.AddHttpClient("ModShelf.Other").ConfigurePrimaryHttpMessageHandler(() => _site);
        _services = services.BuildServiceProvider();

        _credentials.Current[Nexus] = new AccountCredentials("access-1");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ASourcesClientSendsItsLinkedAccountOnce()
    {
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await ClientNamed(SourceHttpClients.NameFor(Nexus)).GetAsync(Endpoint, Cancellation);

        RecordedRequest sent = Assert.Single(_site.Requests);
        Assert.Equal("Bearer access-1", sent.Authorization);
        Assert.Equal("nexus", sent.Headers![ScriptedCredentialProvider.AccountHeader]);
    }

    [Fact]
    public async Task ASourcesClientRetriesARefusalWithRefreshedCredentials()
    {
        _credentials.Refresh = _ => Task.FromResult<AccountCredentials?>(new AccountCredentials("access-2"));
        _site
            .Respond(() => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await ClientNamed(SourceHttpClients.NameFor(Nexus)).GetAsync(Endpoint, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer access-2", _site.Requests[1].Authorization);
    }

    [Fact]
    public async Task OtherClientsAreLeftAlone()
    {
        _site.Respond(() => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await ClientNamed("ModShelf.Other").GetAsync(Endpoint, Cancellation);

        Assert.Null(Assert.Single(_site.Requests).Authorization);
    }

    private HttpClient ClientNamed(string name) => _services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
}
