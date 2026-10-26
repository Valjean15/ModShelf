using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Accounts;

namespace ModShelf.Sources.Nexus.Tests.Accounts;

public sealed class NexusApiKeyStrategyTests
{
    private readonly NexusApiKeyStrategy _strategy = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PointsTheUserToTheirApiKeyPage()
    {
        AuthChallenge challenge = await _strategy.BeginAsync(new AuthStart(), Cancellation);

        Assert.Equal(AuthKind.ApiKey, challenge.Kind);
        Assert.Equal(new Uri("https://next.nexusmods.com/settings/api-keys"), challenge.Url);
    }

    [Fact]
    public async Task TheKeyIsTheCredentials()
    {
        AuthChallenge challenge = await _strategy.BeginAsync(new AuthStart(), Cancellation);

        AccountCredentials credentials = await _strategy.CompleteAsync(challenge, "key-abc+/==", Cancellation);

        Assert.Equal(new AccountCredentials("key-abc+/=="), credentials);
    }

    [Fact]
    public void SendsTheKeyInTheApikeyHeaderOnly()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("https://api.nexusmods.com/v1/users/validate.json"));
        request.Headers.Add("apikey", "an-older-key");

        _strategy.Authorize(request, new AccountCredentials("key-abc+/=="));

        Assert.Equal("key-abc+/==", Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task AKeyCantBeRefreshed()
    {
        Assert.Null(await _strategy.RefreshAsync(new AccountCredentials("key-abc"), Cancellation));
    }
}
