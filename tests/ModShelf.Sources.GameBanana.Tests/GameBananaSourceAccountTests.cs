using System.Net;
using System.Text.Json.Nodes;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// A GameBanana member linked by their public profile: named by its link or their name, read from their profile page,
/// and refused when GameBanana has no such member or doesn't show them.
/// </summary>
public sealed class GameBananaSourceAccountTests : IDisposable
{
    private const string ProfilePath = $"/apiv11/Member/{GameBananaHarness.Member}/ProfilePage";

    private const string NoMembers = """{"_aMetadata":{"_nRecordCount":0,"_bIsComplete":true},"_aRecords":[]}""";

    private readonly GameBananaHarness _harness = new();

    private IAuthStrategy Strategy => Assert.Single(_harness.Source.Strategies);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task LinksAPublicProfileOnlyAndWorksWithoutOne()
    {
        AuthChallenge challenge = await Strategy.BeginAsync(new AuthStart(), GameBananaHarness.Cancellation);

        Assert.True(_harness.Source.WorksWithoutAccount);
        Assert.Equal((AuthKind.PublicProfile, AuthKind.PublicProfile, null), (Strategy.Kind, challenge.Kind, challenge.Url));
        Assert.Equal("subscribed", _harness.Source.Words.Followed);
        Assert.Null(await Strategy.RefreshAsync(new AccountCredentials(GameBananaHarness.Member), GameBananaHarness.Cancellation));
        Assert.Empty(_harness.Api.Requests);
    }

    [Theory]
    [InlineData("https://gamebanana.com/members/1740038")]
    [InlineData("  http://www.GameBanana.com/members/1740038/  ")]
    [InlineData("https://gamebanana.com/members/1740038/subscriptions")]
    [InlineData("gamebanana.com/members/1740038")]
    [InlineData("www.gamebanana.com/members/1740038")]
    public async Task TakesTheMemberOfAProfileLinkWithoutAsking(string link)
    {
        AccountCredentials credentials = await CompleteAsync(link);

        Assert.Equal(GameBananaHarness.Member, credentials.AccessToken);
        Assert.Empty(_harness.Api.Requests);
    }

    [Theory]
    [InlineData("https://gamebanana.com/mods/440088")]
    [InlineData("https://gamebanana.com/members/chlorophylls")]
    [InlineData("https://elsewhere.example/members/1740038")]
    public async Task RefusesALinkToAnythingElseWithoutAsking(string link)
    {
        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => CompleteAsync(link));

        Assert.Equal("That link isn't to a member's profile on GameBanana, as https://gamebanana.com/members/1740038 is.", refused.Message);
        Assert.Empty(_harness.Api.Requests);
    }

    [Theory]
    [InlineData("chlorophylls")]
    [InlineData(" chlorophylls ")]
    public async Task FindsTheMemberOfAName(string name)
    {
        _harness.Api.Serve("member-search");

        AccountCredentials credentials = await CompleteAsync(name);

        Assert.Equal(GameBananaHarness.Member, credentials.AccessToken);
        Assert.Equal(["/apiv11/Util/Search/Results?_sModelName=Member&_sSearchString=chlorophylls"], _harness.Asked.Select(Uri.UnescapeDataString));
    }

    [Fact]
    public async Task FindsTheMemberOfANameWhateverItsCase()
    {
        _harness.Api.Answer("/apiv11/Util/Search/Results", answer => answer.WithBody(RecordedGameBananaApi.Body("member-search").ToJsonString()));

        AccountCredentials credentials = await CompleteAsync("CHLOROPHYLLS");

        Assert.Equal(GameBananaHarness.Member, credentials.AccessToken);
    }

    [Fact]
    public async Task RefusesANameNoMemberHasExactly()
    {
        _harness.Api.Answer("/apiv11/Util/Search/Results", answer => answer.WithBody(RecordedGameBananaApi.Body("member-search").ToJsonString()));

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => CompleteAsync("chlorophyll"));

        Assert.Equal("GameBanana has no member named “chlorophyll”.", refused.Message);
    }

    [Fact]
    public async Task TakesANumberNoMemberIsNamedAsTheMembersId()
    {
        _harness.Api.Answer("/apiv11/Util/Search/Results", answer => answer.WithBody(NoMembers));

        AccountCredentials credentials = await CompleteAsync("1740038");

        Assert.Equal(GameBananaHarness.Member, credentials.AccessToken);
        Assert.Single(_harness.Api.Requests);
    }

    [Fact]
    public async Task TakesTheMemberANumberNamesBeforeTheId()
    {
        _harness.Api.Answer(
            "/apiv11/Util/Search/Results",
            answer => answer.WithBody("""{"_aMetadata":{"_nRecordCount":1,"_bIsComplete":true},"_aRecords":[{"_idRow":42,"_sName":"1740038"}]}"""));

        AccountCredentials credentials = await CompleteAsync("1740038");

        Assert.Equal("42", credentials.AccessToken);
    }

    [Fact]
    public async Task DescribesTheMemberByTheirProfilePage()
    {
        _harness.Api.Serve("member");

        AccountProfile profile = await GetProfileAsync(GameBananaHarness.Member);

        Assert.Equal(
            ("chlorophylls", new Uri("https://images.gamebanana.com/img/av/60b16b9cd22b0.png"), null),
            (profile.DisplayName, profile.AvatarUrl, profile.Tier));
        Assert.Equal([ProfilePath], _harness.Asked);
    }

    [Theory]
    [InlineData("member-not-found")]
    [InlineData("member-not-found-kept")]
    public async Task RefusesAMemberGameBananaDoesntHave(string recording)
    {
        _harness.Api.Serve(recording);

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetProfileAsync("980475433"));

        Assert.Equal("GameBanana has no member 980475433.", refused.Message);
    }

    [Fact]
    public async Task RefusesAPrivateProfile()
    {
        JsonNode body = RecordedGameBananaApi.Body("member");
        body["_bIsPrivate"] = true;
        _harness.Api.Answer(ProfilePath, answer => answer.WithBody(body.ToJsonString()));

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetProfileAsync(GameBananaHarness.Member));

        Assert.Equal("chlorophylls's profile on GameBanana is private, so ModShelf can't read it. Make it public on GameBanana, or link another.", refused.Message);
    }

    [Theory]
    [InlineData("""{"_bIsBanned":true,"_aBanInfo":{"_sLength":"permanent"}}""")]
    [InlineData("""{"_idRow":1740038,"_sName":" "}""")]
    public async Task RefusesAMemberWhoseProfileGameBananaDoesntShow(string page)
    {
        _harness.Api.Answer(ProfilePath, answer => answer.WithBody(page));

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetProfileAsync(GameBananaHarness.Member));

        Assert.Equal("GameBanana doesn't show member 1740038's profile.", refused.Message);
    }

    [Fact]
    public async Task ABannedMemberIsRefusedEvenWithAName()
    {
        JsonNode body = RecordedGameBananaApi.Body("member");
        body["_bIsBanned"] = true;
        _harness.Api.Answer(ProfilePath, answer => answer.WithBody(body.ToJsonString()));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetProfileAsync(GameBananaHarness.Member));
    }

    [Theory]
    [InlineData("chlorophylls")]
    [InlineData("../Mod/440088")]
    public async Task RefusesCredentialsThatArentAMemberWithoutAsking(string credentials)
    {
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => GetProfileAsync(credentials));

        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task SaysWhenGameBananaFails()
    {
        _harness.Api.Answer(ProfilePath, answer => answer.WithStatusCode(HttpStatusCode.ServiceUnavailable));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => GetProfileAsync(GameBananaHarness.Member));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
    }

    [Theory]
    [InlineData("apiv11/Member/linked/Subscriptions?_nPage=1", "apiv11/Member/1740038/Subscriptions?_nPage=1")]
    [InlineData("http://localhost:5000/apiv11/Member/linked/Subscriptions?_nPage=2", "http://localhost:5000/apiv11/Member/1740038/Subscriptions?_nPage=2")]
    [InlineData("apiv11/Mod/440088/ProfilePage", "apiv11/Mod/440088/ProfilePage")]
    [InlineData("apiv11/Member/5/ProfilePage", "apiv11/Member/5/ProfilePage")]
    [InlineData("apiv11/Util/Search/Results?_sSearchString=/Member/linked/", "apiv11/Util/Search/Results?_sSearchString=/Member/linked/")]
    public void NamesTheMemberWhereARequestSaysTheLinkedOne(string asked, string sent)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(asked, UriKind.RelativeOrAbsolute));

        Strategy.Authorize(request, new AccountCredentials(GameBananaHarness.Member));

        Assert.Equal(sent, request.RequestUri!.OriginalString);
        Assert.Empty(request.Headers);
    }

    [Fact]
    public void NamesNoMemberButAnId()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("apiv11/Member/linked/Subscriptions", UriKind.Relative));

        Strategy.Authorize(request, new AccountCredentials("../Mod/440088"));

        Assert.Equal("apiv11/Member/linked/Subscriptions", request.RequestUri!.OriginalString);
    }

    private async Task<AccountCredentials> CompleteAsync(string answer)
    {
        AuthChallenge challenge = await Strategy.BeginAsync(new AuthStart(), GameBananaHarness.Cancellation);
        return await Strategy.CompleteAsync(challenge, answer, GameBananaHarness.Cancellation);
    }

    private Task<AccountProfile> GetProfileAsync(string member)
        => _harness.Source.GetProfileAsync(new AccountCredentials(member), AuthKind.PublicProfile, GameBananaHarness.Cancellation);
}
