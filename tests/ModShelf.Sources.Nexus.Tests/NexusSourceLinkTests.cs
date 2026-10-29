using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Which file a link points to, and what it's called, against the recorded API: SMAPI for Stardew Valley, whose
/// newest main file is 160380.
/// </summary>
public sealed class NexusSourceLinkTests : IDisposable
{
    private const string FilesPath = "/v1/games/stardewvalley/mods/2400/files.json";
    private const string FilePath = "/v1/games/stardewvalley/mods/2400/files/160380.json";
    private const string Key = "nxm-key-5ec2e7";

    private static readonly ModFileRef Smapi = new(new ModRef(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400"), "160380");
    private static readonly string SmapiFileName = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "file")["response"]!["body"]!["file_name"]!.GetValue<string>();

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceLinkTests()
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
    public async Task AFileLinkIsThatFileUnderItsOwnName()
    {
        _api.Serve(RecordedNexusApi.Premium, "file");

        RecognizedLink? link = await Source.RecognizeAsync(
            new Uri("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"), Cancellation);

        Assert.NotNull(link);
        Assert.Equal(Smapi, link.File);
        Assert.Equal(SmapiFileName, link.SuggestedFileName);
        Assert.False(link.IsCatchAll);
        Assert.False(link.IsHandoff);
        Assert.Equal(FilePath, Assert.Single(_api.Requests).RequestMessage!.Path);
    }

    [Fact]
    public async Task AModLinkIsItsNewestMainFile()
    {
        _api.Serve(RecordedNexusApi.Premium, "files");

        RecognizedLink? link = await Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), Cancellation);

        Assert.NotNull(link);
        Assert.Equal(Smapi, link.File);
        Assert.Equal(SmapiFileName, link.SuggestedFileName);
        Assert.Equal(FilesPath, Assert.Single(_api.Requests).RequestMessage!.Path);
    }

    [Fact]
    public async Task TheNewestMainFileIsTheOneUploadedLast()
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBody("""
            {"files": [
              {"file_id": 30, "file_name": "older-main.zip", "category_id": 1, "uploaded_timestamp": 1700000000},
              {"file_id": 10, "file_name": "newest-main.zip", "category_id": 1, "uploaded_timestamp": 1770000000},
              {"file_id": 40, "file_name": "newer-optional.zip", "category_id": 3, "uploaded_timestamp": 1780000000},
              {"file_id": 20, "file_name": "same-time-lower-id.zip", "category_id": 1, "uploaded_timestamp": 1770000000}
            ]}
            """));

        RecognizedLink? link = await Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), Cancellation);

        Assert.Equal("20", link?.File.Id);
        Assert.Equal("same-time-lower-id.zip", link?.SuggestedFileName);
    }

    [Fact]
    public async Task AModWithoutMainFilesNeedsAFileLink()
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBody("""
            {"files": [{"file_id": 40, "file_name": "optional.zip", "category_id": 3, "uploaded_timestamp": 1780000000}]}
            """));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), Cancellation));

        Assert.Contains("Files tab", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnNxmLinkCompletesAHandoffAndItsKeyStaysHere()
    {
        _api.Serve(RecordedNexusApi.Premium, "file");

        RecognizedLink? link = await Source.RecognizeAsync(
            new Uri($"nxm://stardewvalley/mods/2400/files/160380?key={Key}&expires=1792810466&user_id=1000001"), Cancellation);

        Assert.NotNull(link);
        Assert.Equal(Smapi, link.File);
        Assert.True(link.IsHandoff);
        Assert.Equal(SmapiFileName, link.SuggestedFileName);
        Assert.DoesNotContain(Key, Assert.Single(_api.Requests).RequestMessage!.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Logs.Entries, entry => entry.Contains(Key, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SMAPI: 4.5.2?.zip", "SMAPI_ 4.5.2_.zip")]
    [InlineData("SMAPI/4.5.2.zip. ", "SMAPI_4.5.2.zip")]
    [InlineData("  ", null)]
    [InlineData("..", null)]
    [InlineData(null, null)]
    public async Task SuggestsANameEverySystemAccepts(string? fileName, string? suggested)
    {
        _api.Answer(FilePath, response => response.WithStatusCode(200).WithBodyAsJson(new
        {
            file_id = 160380,
            file_name = fileName,
            category_id = 1,
            uploaded_timestamp = 1773515243,
        }));

        RecognizedLink? link = await Source.RecognizeAsync(
            new Uri("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"), Cancellation);

        Assert.Equal(suggested, link?.SuggestedFileName);
    }

    [Fact]
    public async Task AFileThatDoesntExistIsNotFound()
    {
        _api.Answer(FilePath, response => response.WithStatusCode(404).WithBody("""{"error": "File ID '160380' not found"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.RecognizeAsync(
            new Uri("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task LeavesOtherSitesLinksAloneWithoutAskingTheApi()
    {
        Assert.Null(await Source.RecognizeAsync(new Uri("https://example.com/stardewvalley/mods/2400"), Cancellation));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task RefusesADownloadLinkFromItsServersWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source.RecognizeAsync(
            new Uri("https://cf-files.nexusmods.com/cdn/1303/2400/SMAPI.zip?expires=1792810466&md5=abc"), Cancellation));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task RefusesItsOwnLinksToSomethingElseWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/news/12920"), Cancellation));
        Assert.Empty(_api.Requests);
    }
}
