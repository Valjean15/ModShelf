using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Nexus.Tests.Common;

using WireMock.ResponseBuilders;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Download links for SMAPI's file 160380, against the recorded API: straight away for Premium, through the website
/// and an <c>nxm://</c> handoff for free accounts.
/// </summary>
public sealed class NexusSourceDownloadTests : IDisposable
{
    private const string DownloadLinkPath = "/v1/games/stardewvalley/mods/2400/files/160380/download_link.json";
    private const string Key = "nxm-key-5ec2e7";
    private const string Expires = "1792810466";
    private const string CdnSecret = "cdn-signature-77aa";

    private static readonly ModFileRef Smapi = new(new ModRef(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400"), "160380");
    private static readonly Uri Handoff = new($"nxm://stardewvalley/mods/2400/files/160380?key={Key}&expires={Expires}&user_id=1000001");
    private static readonly Uri FilePage = new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380&nmm=1");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceDownloadTests()
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
    public async Task APremiumAccountGetsTheFirstServerStraightAway()
    {
        _api.Serve(RecordedNexusApi.Premium, "download-link");
        string first = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "download-link")["response"]!["body"]![0]!["URI"]!.GetValue<string>();

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, handoff: null, Cancellation));

        Assert.Equal(new Uri(first), ticket.Url);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(long.Parse(Expires, System.Globalization.CultureInfo.InvariantCulture)), ticket.ExpiresAt);
        Assert.Empty(ticket.Headers);
        Assert.Null(ticket.ExpectedHash);
        Assert.Equal(DownloadLinkPath, Assert.Single(_api.Requests).RequestMessage!.Path);
    }

    [Fact]
    public async Task AFreeAccountIsSentToTheFilesPage()
    {
        _api.Serve(RecordedNexusApi.Free, "download-link-without-key");

        HandoffRequired handoff = Assert.IsType<HandoffRequired>(await Source.ResolveAsync(Smapi, handoff: null, Cancellation));

        Assert.Equal(FilePage, handoff.PageUrl);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AFreeAccountsHandoffGetsTheFileAsNexusModsAnswered()
    {
        _api.Serve(RecordedNexusApi.Free, "download-link-with-nxm-key");
        string first = RecordedNexusApi.Recording(RecordedNexusApi.Free, "download-link-with-nxm-key")["response"]!["body"]![0]!["URI"]!.GetValue<string>();

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(
            await Source.ResolveAsync(Smapi, new Uri(RecordedNexusApi.RecordedText(RecordedNexusApi.Free, "nxm-link.txt")), Cancellation));

        Assert.Equal(new Uri(first), ticket.Url);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AKeyNexusModsNeverGaveIsRefusedAndTheAccountSentToTheFilesPage()
    {
        // Nexus Mods refuses the made-up key with 400, then the free account without a key with 403.
        _api.Serve(RecordedNexusApi.Free, "download-link-without-key");
        _api.Serve(RecordedNexusApi.Free, "download-link-with-wrong-nxm-key");

        HandoffRequired handoff = Assert.IsType<HandoffRequired>(await Source.ResolveAsync(
            Smapi, new Uri("nxm://stardewvalley/mods/2400/files/160380?key=not-a-real-nxm-key&expires=1793560033&user_id=1000002"), Cancellation));

        Assert.Equal(FilePage, handoff.PageUrl);
        Assert.Equal([400, 403], _api.Requests.Select(request => (int)request.ResponseMessage!.StatusCode!));
    }

    [Fact]
    public async Task SendsTheKeyAndExpiryOfTheHandoff()
    {
        _api.Answer(DownloadLinkPath, Servers);

        Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, Handoff, Cancellation));

        IDictionary<string, WireMock.Types.WireMockList<string>> query = Assert.Single(_api.Requests).RequestMessage!.Query!;
        Assert.Equal(Key, Assert.Single(query["key"]));
        Assert.Equal(Expires, Assert.Single(query["expires"]));
        Assert.False(query.ContainsKey("user_id"));
    }

    [Fact]
    public async Task EscapesTheKey()
    {
        _api.Answer(DownloadLinkPath, Servers);

        await Source.ResolveAsync(Smapi, new Uri("nxm://stardewvalley/mods/2400/files/160380?key=a%2Bb%26c%3Dd&expires=1792810466"), Cancellation);

        Assert.Equal("a+b&c=d", Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Query!["key"]));
    }

    [Theory]
    [InlineData("nxm://stardewvalley/mods/2400/files/160380")]
    [InlineData("nxm://stardewvalley/mods/2400/files/160380?key=&expires=1792810466")]
    [InlineData("nxm://stardewvalley/mods/2400/files/160380?key=abc")]
    [InlineData("nxm://stardewvalley/mods/2400/files/160380?key=abc&expires=soon")]
    public async Task AHandoffWithoutAUsableKeyIsLikeNone(string handoff)
    {
        _api.Answer(DownloadLinkPath, Servers);

        Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, new Uri(handoff), Cancellation));

        Assert.Empty(Assert.Single(_api.Requests).RequestMessage!.Query ?? new Dictionary<string, WireMock.Types.WireMockList<string>>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task TriesTheAccountWithoutAKeyNexusModsRefused(HttpStatusCode refusal)
    {
        _api.AnswerInTurn(DownloadLinkPath, response => Refused(response, refusal), Servers);

        Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, Handoff, Cancellation));

        Assert.Collection(
            _api.Requests,
            withKey => Assert.True(withKey.RequestMessage!.Query!.ContainsKey("key")),
            withoutKey => Assert.False(withoutKey.RequestMessage!.Query?.ContainsKey("key") ?? false));
    }

    [Fact]
    public async Task AsksForANewHandoffWhenAFreeAccountsKeyIsRefused()
    {
        _api.AnswerInTurn(DownloadLinkPath, response => Refused(response, HttpStatusCode.Forbidden));

        HandoffRequired handoff = Assert.IsType<HandoffRequired>(await Source.ResolveAsync(Smapi, Handoff, Cancellation));

        Assert.Equal(FilePage, handoff.PageUrl);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task OtherRefusalsReachTheEngineAsTheyAre(HttpStatusCode refusal)
    {
        _api.Answer(DownloadLinkPath, response => Refused(response, refusal));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.ResolveAsync(Smapi, Handoff, Cancellation));

        Assert.Equal(refusal, refused.StatusCode);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task ARefusedAccountReachesTheEngineAsSuch()
    {
        _api.Answer(DownloadLinkPath, response => Refused(response, HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Source.ResolveAsync(Smapi, handoff: null, Cancellation));
    }

    [Fact]
    public async Task AFreeAccountWithAGoneFileIsntSentToTheWebsite()
    {
        _api.Answer(DownloadLinkPath, response => Refused(response, HttpStatusCode.Gone));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.ResolveAsync(Smapi, handoff: null, Cancellation));

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
    }

    [Fact]
    public async Task SkipsServersWithoutAWebLink()
    {
        _api.Answer(DownloadLinkPath, response => response.WithStatusCode(200).WithBody($$"""
            [
              {"name": "Broken", "short_name": "Broken", "URI": null},
              {"name": "Elsewhere", "short_name": "Elsewhere", "URI": "ftp://files.example.com/SMAPI.zip"},
              {"name": "Amsterdam", "short_name": "Amsterdam", "URI": "https://amsterdam.nexus-cdn.com/1303/2400/SMAPI.zip?md5={{CdnSecret}}"}
            ]
            """));

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, handoff: null, Cancellation));

        Assert.Equal("amsterdam.nexus-cdn.com", ticket.Url.Host);
        Assert.Null(ticket.ExpiresAt);
    }

    [Fact]
    public async Task FailsWhenNoServerIsOffered()
    {
        _api.Answer(DownloadLinkPath, response => response.WithStatusCode(200).WithBody("[]"));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Source.ResolveAsync(Smapi, handoff: null, Cancellation));

        Assert.Null(failed.StatusCode);
    }

    [Fact]
    public async Task AsksAgainEveryTime()
    {
        _api.Answer(DownloadLinkPath, Servers);

        await Source.ResolveAsync(Smapi, handoff: null, Cancellation);
        await Source.ResolveAsync(Smapi, handoff: null, Cancellation);

        Assert.Equal(2, _api.Requests.Count);
    }

    [Theory]
    [InlineData("nxm://stardewvalley/mods/2400/files/160381?key=abc&expires=1792810466")]
    [InlineData("nxm://skyrimspecialedition/mods/2400/files/160380?key=abc&expires=1792810466")]
    [InlineData("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380")]
    [InlineData("https://example.com/SMAPI.zip?key=abc&expires=1792810466")]
    public async Task RefusesAHandoffForSomethingElse(string handoff)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source.ResolveAsync(Smapi, new Uri(handoff), Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task RefusesAFileWithIdsNexusModsDoesntGive()
    {
        ModFileRef file = new(new ModRef(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400"), "160380/../../users");

        await Assert.ThrowsAsync<ArgumentException>(() => Source.ResolveAsync(file, handoff: null, Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task NeverLogsTheKeyOrTheDownloadLink()
    {
        _api.AnswerInTurn(DownloadLinkPath, response => Refused(response, HttpStatusCode.Forbidden), Servers);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(await Source.ResolveAsync(Smapi, Handoff, Cancellation));

        Assert.NotEmpty(_api.Logs.Entries);
        Assert.DoesNotContain(_api.Logs.Entries, entry => entry.Contains(Key, StringComparison.Ordinal));
        Assert.DoesNotContain(_api.Logs.Entries, entry => entry.Contains(CdnSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(Key, ticket.ToString(), StringComparison.Ordinal);
    }

    private static IResponseBuilder Servers(IResponseBuilder response)
        => response.WithStatusCode(200).WithBody($$"""
            [
              {"name": "Nexus Global Content Delivery Network", "short_name": "Nexus CDN", "URI": "https://cf-files.nexusmods.com/cdn/1303/2400/SMAPI.zip?expires=1792810466&md5={{CdnSecret}}&user_id=1000001"},
              {"name": "Amsterdam (Premium)", "short_name": "Amsterdam", "URI": "https://amsterdam-premium.nexus-cdn.com/1303/2400/SMAPI.zip?expires=1792810466&md5={{CdnSecret}}&user_id=1000001"}
            ]
            """);

    private static IResponseBuilder Refused(IResponseBuilder response, HttpStatusCode status)
        => response.WithStatusCode((int)status).WithBody($$"""{"code": {{(int)status}}, "message": "Refused."}""");
}
