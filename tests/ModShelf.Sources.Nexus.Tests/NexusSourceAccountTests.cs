using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Linking accounts: the ways the source offers, and whose account a key belongs to, against the recorded API.
/// </summary>
public sealed class NexusSourceAccountTests : IDisposable
{
    private const string Key = "key-from-the-user";

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceAccountTests()
    {
        _services = _api.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public void NeedsAnAccountAndLinksWithAnApiKey()
    {
        Assert.False(Source.WorksWithoutAccount);
        Assert.Equal([AuthKind.ApiKey], Source.Strategies.Select(strategy => strategy.Kind));
    }

    [Theory]
    [InlineData(RecordedNexusApi.Premium, "Premium")]
    [InlineData(RecordedNexusApi.Free, "Free")]
    public async Task AKeyBelongsToTheAccountTheApiNames(string account, string tier)
    {
        _api.Serve(account, "validate");
        JsonNode user = RecordedNexusApi.Recording(account, "validate")["response"]!["body"]!;

        AccountProfile profile = await Source.GetProfileAsync(new AccountCredentials(Key), AuthKind.ApiKey, Cancellation);

        Assert.Equal(user["name"]!.GetValue<string>(), profile.DisplayName);
        Assert.Equal(new Uri(user["profile_url"]!.GetValue<string>()), profile.AvatarUrl);
        Assert.Equal(tier, profile.Tier);
        Assert.Equal(Key, Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Headers!["apikey"]));
    }

    [Fact]
    public async Task ASupporterWithoutPremiumIsASupporter()
    {
        _api.Answer("/v1/users/validate.json", response => response.WithStatusCode(200).WithBody(
            """{"user_id": 7, "name": "Sam", "is_premium": false, "is_supporter": true, "profile_url": "https://avatars.nexusmods.com/7/100"}"""));

        AccountProfile profile = await Source.GetProfileAsync(new AccountCredentials(Key), AuthKind.ApiKey, Cancellation);

        Assert.Equal("Supporter", profile.Tier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("ftp://avatars.nexusmods.com/7/100")]
    public async Task AProfileWithoutAUsableAvatarHasNone(string? profileUrl)
    {
        string url = profileUrl is null ? "null" : $"\"{profileUrl}\"";
        _api.Answer("/v1/users/validate.json", response => response.WithStatusCode(200).WithBody(
            $$"""{"user_id": 7, "name": "Sam", "is_premium": false, "is_supporter": false, "profile_url": {{url}}}"""));

        AccountProfile profile = await Source.GetProfileAsync(new AccountCredentials(Key), AuthKind.ApiKey, Cancellation);

        Assert.Null(profile.AvatarUrl);
    }

    [Fact]
    public async Task AKeyTheApiRefusesIsAnAuthenticationFailure()
    {
        _api.Serve(RecordedNexusApi.Premium, "validate-invalid-key");

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => Source.GetProfileAsync(new AccountCredentials(Key), AuthKind.ApiKey, Cancellation));
    }
}
