using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// An API key or an access token, checked with mod.io, whose a token is, and the subscriptions a token reads; neither
/// ever written in the log.
/// </summary>
public sealed class ModIoSourceAccountTests : IDisposable
{
    private readonly ModIoHarness _harness = new();

    private IAuthStrategy Strategy => Assert.Single(_harness.Source.Strategies);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task LinksAKeyOrATokenAndWorksWithNeither()
    {
        AuthChallenge challenge = await Strategy.BeginAsync(new AuthStart(), ModIoHarness.Cancellation);

        Assert.False(_harness.Source.WorksWithoutAccount);
        Assert.Equal((AuthKind.ApiKey, AuthKind.ApiKey, new Uri("https://mod.io/me/access")), (Strategy.Kind, challenge.Kind, challenge.Url));
        Assert.Equal(("subscribed", (string?)null, (string?)null), (_harness.Source.Words.Followed, _harness.Source.Words.Follow, _harness.Source.Words.Unfollow));
        Assert.Equal(("positive rating", "positive ratings"), (_harness.Source.Words.Approval, _harness.Source.Words.Approvals));
        Assert.Null(await Strategy.RefreshAsync(new AccountCredentials(RecordedModIoApi.ApiKey), ModIoHarness.Cancellation));
        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task AnAccessTokenIsItsOwnersAccount()
    {
        _harness.Api.ServeAll();

        AccountProfile profile = await _harness.Source.GetProfileAsync(new AccountCredentials(RecordedModIoApi.AccessToken), AuthKind.ApiKey, ModIoHarness.Cancellation);

        Assert.Equal(("ModShelfTester", new Uri("https://example.com/avatar.png"), "Access token"), (profile.DisplayName, profile.AvatarUrl, profile.Tier));
        Assert.Equal("/v1/me", Assert.Single(_harness.Asked));
        Assert.Equal($"Bearer {RecordedModIoApi.AccessToken}", _harness.Api.Requests[0].RequestMessage!.Headers!["Authorization"].Single());
    }

    [Fact]
    public async Task AnAccessTokenWhoseOwnerHasNoNameIsAModIoAccount()
    {
        _harness.Api.Answer("/v1/me", answer => answer.WithBody("""{"id": 1, "username": " ", "avatar": {"original": "ftp://example.com/a.png"}}"""));

        AccountProfile profile = await _harness.Source.GetProfileAsync(new AccountCredentials(RecordedModIoApi.AccessToken), AuthKind.ApiKey, ModIoHarness.Cancellation);

        Assert.Equal(("mod.io account", (Uri?)null), (profile.DisplayName, profile.AvatarUrl));
    }

    [Fact]
    public async Task AnApiKeyIsCheckedByAskingForAGameAndNamedForWhatItIs()
    {
        _harness.Api.ServeAll();

        AccountProfile profile = await _harness.Source.GetProfileAsync(new AccountCredentials(RecordedModIoApi.ApiKey), AuthKind.ApiKey, ModIoHarness.Cancellation);

        Assert.Equal(("API key", (Uri?)null, "Without subscriptions"), (profile.DisplayName, profile.AvatarUrl, profile.Tier));
        Assert.Equal("/v1/games?_limit=1", Assert.Single(_harness.Asked));
        Assert.Equal($"?_limit=1&api_key={RecordedModIoApi.ApiKey}", _harness.Api.Requests[0].RequestMessage!.RawQuery);
        Assert.False(_harness.Api.Requests[0].RequestMessage!.Headers!.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task ARefusedTokenSaysWhy()
    {
        _harness.Api.Answer("/v1/me", "me-refused");

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _harness.Source.GetProfileAsync(new AccountCredentials(RecordedModIoApi.AccessToken), AuthKind.ApiKey, ModIoHarness.Cancellation));

        Assert.Equal(
            "mod.io refused the API key or token linked, or none is linked: The supplied access token has either been revoked, has expired or is malformed. Please generate a new one.",
            refused.Message);
    }

    [Fact]
    public async Task ARefusedKeySaysWhy()
    {
        _harness.Api.Answer("/v1/games", "games-refused-key");

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _harness.Source.GetProfileAsync(new AccountCredentials(RecordedModIoApi.ApiKey), AuthKind.ApiKey, ModIoHarness.Cancellation));

        Assert.StartsWith("mod.io refused the API key or token linked, or none is linked: Invalid credentials.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecordedModIoApi.ApiKey, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsNoSubscriptionsWithoutAnAccount()
    {
        _harness.Api.Answer("/v1/me/subscribed", "subscribed-key");

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _harness.Source.GetWatchedAsync(ModIoHarness.Cancellation));
    }

    [Fact]
    public async Task ReadsTheSubscriptionsOfAToken()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        linked.Api.ServeAll();

        IReadOnlyList<ModRef> subscribed = await linked.Source.GetWatchedAsync(ModIoHarness.Cancellation);

        Assert.Empty(subscribed);
        Assert.Equal("/v1/me/subscribed?_limit=100", Assert.Single(linked.Asked));
    }

    [Fact]
    public async Task EachSubscribedModIsOfItsOwnGameAndListedOnce()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        linked.Api.Answer("/v1/me/subscribed", answer => answer.WithBody("""
            {"data": [
              {"id": 4228735, "game_id": 6715}, {"id": 9, "game_id": 306}, {"id": 4228735, "game_id": 6715}, {"id": 10, "game_id": 0}, {"id": 0, "game_id": 6715}
            ], "result_total": 5}
            """));

        IReadOnlyList<ModRef> subscribed = await linked.Source.GetWatchedAsync(ModIoHarness.Cancellation);

        Assert.Equal([ModIoHarness.Mod, new ModRef(new GameRef(ModIoHarness.ModIo, "306"), "9")], subscribed);
    }

    [Fact]
    public async Task ReadsEveryPageOfSubscriptionsUpToTen()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "game_id": 6715 }"""))}}], "result_total": 5000}""";
        linked.Api.Answer("/v1/me/subscribed", answer => answer.WithBody(page));

        IReadOnlyList<ModRef> subscribed = await linked.Source.GetWatchedAsync(ModIoHarness.Cancellation);

        Assert.Equal(100, subscribed.Count);
        Assert.Equal(10, linked.Asked.Count);
        Assert.Equal("/v1/me/subscribed?_limit=100&_offset=900", linked.Asked[^1]);
    }

    [Fact]
    public async Task AFullPageHoldingTheLastSubscriptionIsTheLast()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "game_id": 6715 }"""))}}], "result_total": 100}""";
        linked.Api.Answer("/v1/me/subscribed", answer => answer.WithBody(page));

        await linked.Source.GetWatchedAsync(ModIoHarness.Cancellation);

        Assert.Single(linked.Asked);
    }

    [Fact]
    public async Task APageOfSubscriptionsShortOfFullIsTheLast()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        linked.Api.Answer("/v1/me/subscribed", answer => answer.WithBody("""{"data": [{"id": 1, "game_id": 6715}], "result_total": 3}"""));

        await linked.Source.GetWatchedAsync(ModIoHarness.Cancellation);

        Assert.Single(linked.Asked);
    }

    [Fact]
    public async Task NeverAsksForSubscriptionsWithAnApiKeySoTheKeyStaysLinked()
    {
        using ModIoHarness keyed = new(RecordedModIoApi.ApiKey);
        keyed.Api.ServeAll();
        keyed.Api.Serve("subscribed-key");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => keyed.Source.GetWatchedAsync(ModIoHarness.Cancellation));

        Assert.Equal(
            (HttpStatusCode.Forbidden, "mod.io lists them for an access token only: link one in place of your API key."),
            (refused.StatusCode, refused.Message));
        Assert.Empty(keyed.Api.Requests);
        Assert.Equal(0, keyed.Linked!.Refreshes);
    }

    [Fact]
    public async Task ATokenModIoRefusesIsToBeLinkedAgain()
    {
        using ModIoHarness linked = new(RecordedModIoApi.AccessToken);
        linked.Api.Answer("/v1/me/subscribed", "subscribed-key");

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => linked.Source.GetWatchedAsync(ModIoHarness.Cancellation));

        Assert.Equal(1, linked.Linked!.Refreshes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeverWritesTheKeyOrTokenInTheLog(bool key)
    {
        string secret = key ? RecordedModIoApi.ApiKey : RecordedModIoApi.AccessToken;
        using ModIoHarness linked = new(secret);
        linked.Api.ServeAll();

        await linked.Source.SearchAsync(ModIoHarness.Game, new SearchQuery("better", count: 50), ModIoHarness.Cancellation);
        await linked.Source.ResolveAsync(new ModFileRef(ModIoHarness.Mod, RecordedModIoApi.LiveFile), handoff: null, ModIoHarness.Cancellation);
        await linked.Source.GetProfileAsync(new AccountCredentials(secret), AuthKind.ApiKey, ModIoHarness.Cancellation);
        linked.Api.Answer("/v1/games/6715/mods/4228735", "mod-not-found");
        await Assert.ThrowsAsync<HttpRequestException>(() => linked.Source.GetModAsync(ModIoHarness.Mod, ModIoHarness.Cancellation));

        // The client factory writes each request's address, without its query, and its headers, without their values.
        Assert.Contains(linked.Api.Logs.Entries, entry => entry.Contains("/v1/games/6715/mods?*", StringComparison.Ordinal));
        Assert.DoesNotContain(linked.Api.Logs.Entries, entry => entry.Contains(secret[..16], StringComparison.Ordinal));
    }
}
