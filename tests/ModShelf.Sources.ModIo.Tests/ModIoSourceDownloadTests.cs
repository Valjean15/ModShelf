using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// Where a file downloads from: mod.io's link for it, read afresh, asked where it sends the download on to, with the
/// file's checksum; never a file mod.io's virus scan found malicious.
/// </summary>
public sealed class ModIoSourceDownloadTests : IDisposable
{
    private const string FilePath = "/v1/games/6715/mods/4228735/files/6795889";

    private static readonly ModFileRef Live = new(ModIoHarness.Mod, RecordedModIoApi.LiveFile);

    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public ModIoSourceDownloadTests() => _harness.Api.ServeAll();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DownloadsFromWhereModIosLinkSendsItWithItsChecksum()
    {
        DownloadTicket ticket = Assert.IsType<DownloadTicket>(await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(new Uri("https://binary.modcdn.io/mods/7d37/4228735/betterinventoryui_6b585be8-ed7-bplo.zip"), ticket.Url);
        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, "e9be965442ab4ad811b9cb93f305f91b"), ticket.ExpectedHash);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1857558900), ticket.ExpiresAt);
        Assert.Empty(ticket.Headers);
        Assert.Equal([FilePath, "/v1/games/6715", FilePath + "/download"], _harness.Asked);
    }

    [Fact]
    public async Task AsksWhatAGameLetsAppsDoOnceAndTheFileEachTime()
    {
        await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation);
        await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation);

        Assert.Equal([FilePath, "/v1/games/6715", FilePath + "/download", FilePath, FilePath + "/download"], _harness.Asked);
    }

    [Fact]
    public async Task AGameReadBeforeIsntAskedAgain()
    {
        await _harness.Source.GetGameAsync(ModIoHarness.Game, ModIoHarness.Cancellation);

        await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation);

        Assert.Equal(["/v1/games/6715", FilePath, FilePath + "/download"], _harness.Asked);
    }

    [Fact]
    public async Task RefusesAFileTheVirusScanFoundMalicious()
    {
        _harness.Api.Answer(FilePath, answer => answer.WithBody(File(virus: 1)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(
            (HttpStatusCode.Gone, "mod.io's virus scan found the file malicious, so ModShelf doesn't download it."),
            (refused.StatusCode, refused.Message));
        Assert.Equal([FilePath], _harness.Asked);
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains("found file 6795889 of mod 4228735 malicious", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadsAFileThatMayBeHarmfulSayingSoInTheLog()
    {
        _harness.Api.Answer(FilePath, answer => answer.WithBody(File(virus: 2)));

        Assert.IsType<DownloadTicket>(await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains("says file 6795889 of mod 4228735 may be harmful", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://g-6715.modapi.io.example.com/v1/games/6715/mods/4228735/files/6795889/download")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6795889/download")]
    [InlineData("https://binary.modcdn.io/mods/7d37/4228735/file.zip")]
    [InlineData("file:///C:/file.zip")]
    [InlineData("")]
    public async Task NeverSendsTheAccountToALinkOffModIosApi(string link)
    {
        _harness.Api.Answer(FilePath, answer => answer.WithBody(File(virus: 0, link: link)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal((HttpRequestError.InvalidResponse, "mod.io gave no link on its API to download the file by."), (refused.HttpRequestError, refused.Message));
        Assert.Equal([FilePath], _harness.Asked);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("path")]
    [InlineData("scheme")]
    public async Task NeverSendsTheAccountToAnotherHostPortPathOrScheme(string other)
    {
        UriBuilder elsewhere = new(new Uri(_harness.Api.ApiAddress, "games/6715/mods/4228735/files/6795889/download"));
        switch (other)
        {
            case "host":
                elsewhere.Host = elsewhere.Host == "localhost" ? "127.0.0.1" : "localhost";
                break;
            case "port":
                elsewhere.Port++;
                break;
            case "path":
                elsewhere.Path = "/v2/games/6715/mods/4228735/files/6795889/download";
                break;
            default:
                elsewhere.Scheme = Uri.UriSchemeHttps;
                break;
        }

        _harness.Api.Answer(FilePath, answer => answer.WithBody(File(virus: 0, link: elsewhere.Uri.AbsoluteUri)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(HttpRequestError.InvalidResponse, refused.HttpRequestError);
        Assert.Equal([FilePath], _harness.Asked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALinkOnTheGamesApiOrModIosIsAsked(bool onTheGames)
    {
        using RecordedModIoApi api = new();
        api.ServeAll();
        Uri games = new(api.BaseAddress, "games-host/v1/");
        using ServiceProvider services = api.BuildServices(
            retries: 0,
            linked: RecordedModIoApi.AccessToken,
            configure: options => onTheGames ? options with { ApiAddress = new Uri(api.BaseAddress, "elsewhere/v1/") } : options with { GameApiAddress = games.AbsoluteUri });
        if (!onTheGames)
        {
            api.Answer("/games-host/v1/games/6715", answer => answer.WithBody("""{"id": 6715, "name": "Game", "api_access_options": 3}"""));
            api.Answer("/games-host/v1" + FilePath[3..], answer => answer.WithBody(
                $$"""{"id": 6795889, "download": {"binary_url": "{{api.ApiAddress}}games/6715/mods/4228735/files/6795889/download"} }"""));
        }

        ModIoSource source = services.GetServices<Abstractions.IModSource>().OfType<ModIoSource>().Single();

        Assert.IsType<DownloadTicket>(await source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));
        Assert.Equal("/v1" + FilePath[3..] + "/download", api.Requests[^1].RequestMessage!.Path);
    }

    [Fact]
    public async Task AFileThatDoesntExistIsNotFound()
    {
        _harness.Api.Answer(FilePath, "file-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ADownloadModIoDoesntSendOnIsRefused()
    {
        _harness.Api.Answer(FilePath + "/download", answer => answer.WithStatusCode(HttpStatusCode.OK).WithBody("{}"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal((HttpRequestError.InvalidResponse, "mod.io didn't say where to download the file from."), (refused.HttpRequestError, refused.Message));
    }

    [Fact]
    public async Task ADownloadSentOnToAnythingButTheWebIsRefused()
    {
        _harness.Api.Answer(FilePath + "/download", answer => answer.WithStatusCode(HttpStatusCode.Found).WithHeader("Location", "ftp://binary.modcdn.io/file.zip"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(HttpRequestError.InvalidResponse, refused.HttpRequestError);
    }

    [Fact]
    public async Task ADownloadSentOnToAPlaceOnTheApiIsMadeWhole()
    {
        _harness.Api.Answer(FilePath + "/download", answer => answer.WithStatusCode(HttpStatusCode.Found).WithHeader("Location", "/files/6795889.zip"));

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal(new Uri(_harness.Api.BaseAddress, "files/6795889.zip"), ticket.Url);
    }

    [Theory]
    [InlineData("6795889x")]
    [InlineData("0")]
    public async Task RefusesAFileNamedOtherThanByNumber(string file)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.ResolveAsync(new ModFileRef(ModIoHarness.Mod, file), handoff: null, ModIoHarness.Cancellation));

        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task AGameThatGivesFilesToATokenOnlyIsAskedWithOne()
    {
        _harness.Api.Answer("/v1/games/6715", answer => answer.WithBody("""{"id": 6715, "name": "Game", "api_access_options": 5}"""));

        Assert.IsType<DownloadTicket>(await _harness.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.StartsWith("Bearer ", _harness.Api.Requests[^1].RequestMessage!.Headers!["Authorization"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGameThatGivesFilesToATokenOnlyIsntAskedWithAnApiKey()
    {
        using ModIoHarness keyed = new(RecordedModIoApi.ApiKey);
        keyed.Api.ServeAll();
        keyed.Api.Answer("/v1/games/6715", answer => answer.WithBody("""{"id": 6715, "name": "Game", "api_access_options": 7}"""));

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => keyed.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Equal("mod.io gives this game's files to an access token only: link one in place of your API key.", refused.Message);
        Assert.Equal([FilePath, "/v1/games/6715"], keyed.Asked);
        Assert.Equal(0, keyed.Linked!.Refreshes);
    }

    [Fact]
    public async Task AGameWithDirectDownloadsIsAskedWithAnApiKey()
    {
        using ModIoHarness keyed = new(RecordedModIoApi.ApiKey);
        keyed.Api.ServeAll();

        Assert.IsType<DownloadTicket>(await keyed.Source.ResolveAsync(Live, handoff: null, ModIoHarness.Cancellation));

        Assert.Contains($"api_key={RecordedModIoApi.ApiKey}", keyed.Api.Requests[^1].RequestMessage!.RawQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// The live file, as mod.io describes it, with its link to download it by on the API here unless another is given.
    /// </summary>
    private string File(int virus, string? link = null)
        => $$"""
            {"id": 6795889, "mod_id": 4228735, "virus_positive": {{virus}}, "filehash": {"md5": "e9be965442ab4ad811b9cb93f305f91b"},
             "download": {"binary_url": "{{link ?? $"{_harness.Api.ApiAddress}games/6715/mods/4228735/files/6795889/download"}}", "date_expires": 1857556001} }
            """;
}
