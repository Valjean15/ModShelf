using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// Links to a mod's page, looked up by the names in them, and the API's links to a mod or a file, read without asking.
/// </summary>
public sealed class ModIoSourceLinkTests : IDisposable
{
    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public ModIoSourceLinkTests() => _harness.Api.ServeAll();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("https://mod.io/g/baldursgate3/m/better-inventory-ui1")]
    [InlineData("https://mod.io/g/baldursgate3/m/better-inventory-ui1/")]
    [InlineData("https://www.mod.io/g/BaldursGate3/m/Better-Inventory-UI1#description")]
    [InlineData("http://mod.io/g/baldursgate3/m/better-inventory-ui1/comments?page=2")]
    public async Task AModsPageIsItsLiveFileLookedUpByName(string link)
    {
        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri(link), ModIoHarness.Cancellation);

        Assert.Equal(new ModFileRef(ModIoHarness.Mod, RecordedModIoApi.LiveFile), found?.File);
        Assert.Equal(("betterinventoryui_6b585be8-ed7-bplo.zip", false, false), (found!.SuggestedFileName, found.IsCatchAll, found.IsHandoff));
        Assert.Equal(["/v1/games?name_id=baldursgate3", "/v1/games/6715/mods?name_id=better-inventory-ui1"], _harness.Asked);
    }

    [Fact]
    public async Task AGamesIdIsLookedUpOnceAModsEachTime()
    {
        Uri page = new("https://mod.io/g/baldursgate3/m/better-inventory-ui1");

        await _harness.Source.RecognizeAsync(page, ModIoHarness.Cancellation);
        await _harness.Source.RecognizeAsync(page, ModIoHarness.Cancellation);

        Assert.Equal(
            ["/v1/games?name_id=baldursgate3", "/v1/games/6715/mods?name_id=better-inventory-ui1", "/v1/games/6715/mods?name_id=better-inventory-ui1"],
            _harness.Asked);
    }

    [Theory]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6278773/download")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6278773")]
    [InlineData("https://api.mod.io/v1/games/6715/mods/4228735/files/6278773/download")]
    [InlineData("https://u-1000001.modapi.io/v1/games/6715/mods/4228735/files/6278773/download?hash=x")]
    public async Task AnApiLinkToAFileIsThatFileWithoutAsking(string link)
    {
        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri(link), ModIoHarness.Cancellation);

        Assert.Equal(new ModFileRef(ModIoHarness.Mod, RecordedModIoApi.PreviousFile), found?.File);
        Assert.Null(found!.SuggestedFileName);
        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task AnApiLinkToAModIsItsLiveFile()
    {
        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri("https://g-6715.modapi.io/v1/games/6715/mods/4228735"), ModIoHarness.Cancellation);

        Assert.Equal(new ModFileRef(ModIoHarness.Mod, RecordedModIoApi.LiveFile), found?.File);
        Assert.Equal("betterinventoryui_6b585be8-ed7-bplo.zip", found!.SuggestedFileName);
        Assert.Equal(["/v1/games/6715/mods/4228735"], _harness.Asked);
    }

    [Theory]
    [InlineData("https://gamebanana.com/mods/440088")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("https://binary.modcdn.io/mods/7d37/4228735/betterinventoryui_6b585be8-ed7-bplo.zip")]
    [InlineData("https://mod.io.example.com/g/baldursgate3/m/better-inventory-ui1")]
    [InlineData("https://g-6715.modapi.io.example.com/v1/games/6715/mods/4228735/files/6278773")]
    [InlineData("ftp://mod.io/g/baldursgate3/m/better-inventory-ui1")]
    public async Task ALinkElsewhereIsntItsOwn(string link)
    {
        Assert.Null(await _harness.Source.RecognizeAsync(new Uri(link), ModIoHarness.Cancellation));
        Assert.Empty(_harness.Api.Requests);
    }

    [Theory]
    [InlineData("https://mod.io/g/baldursgate3")]
    [InlineData("https://mod.io/g/baldursgate3/m")]
    [InlineData("https://mod.io/g/baldursgate3/m/bad%20name")]
    [InlineData("https://mod.io/u/modshelf-tester")]
    [InlineData("https://mod.io/")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6278773/delete")]
    [InlineData("https://api.mod.io/v1/games/6715/mods/0/files/6278773")]
    [InlineData("https://api.mod.io/v1/games/abc/mods/4228735")]
    public async Task ALinkToSomethingElseOfModIosIsRefusedWithoutAsking(string link)
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.RecognizeAsync(new Uri(link), ModIoHarness.Cancellation));

        Assert.Equal("That mod.io link isn't to a mod: copy the link of a mod's page. (Parameter 'link')", refused.Message);
        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task AGameIsTheOneOfThatNameAmongThoseFound()
    {
        _harness.Api.Answer("/v1/games", answer => answer.WithBody(
            """{"data": [{"id": 1, "name_id": "baldursgate"}, {"id": 0, "name_id": "baldursgate3"}, {"id": 6715, "name_id": "BaldursGate3"}], "result_total": 3}"""));

        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri("https://mod.io/g/baldursgate3/m/better-inventory-ui1"), ModIoHarness.Cancellation);

        Assert.Equal(ModIoHarness.Mod, found?.File.Mod);
    }

    [Fact]
    public async Task AGameModIoDoesntHaveIsRefused()
    {
        _harness.Api.Answer("/v1/games", answer => answer.WithBody("""{"data": [], "result_total": 0}"""));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.RecognizeAsync(new Uri("https://mod.io/g/nosuchgame/m/better-inventory-ui1"), ModIoHarness.Cancellation));

        Assert.StartsWith("mod.io has no game 'nosuchgame'.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModModIoDoesntHaveIsRefusedAsPerhapsRenamed()
    {
        _harness.Api.Answer("/v1/games/6715/mods", answer => answer.WithBody(
            """{"data": [{"id": 1, "game_id": 6715, "name_id": "better-inventory-ui2"}], "result_total": 1}"""));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.RecognizeAsync(new Uri("https://mod.io/g/baldursgate3/m/better-inventory-ui1"), ModIoHarness.Cancellation));

        Assert.StartsWith("mod.io has no mod 'better-inventory-ui1' of that game: its author may have renamed it.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModWithoutAFileForWindowsIsRefused()
    {
        _harness.Api.Answer("/v1/games/6715/mods", answer => answer.WithBody(
            """{"data": [{"id": 4228735, "game_id": 6715, "name_id": "better-inventory-ui1", "modfile": {}, "platforms": [{"platform": "ps5", "modfile_live": 2}]}], "result_total": 1}"""));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.RecognizeAsync(new Uri("https://mod.io/g/baldursgate3/m/better-inventory-ui1"), ModIoHarness.Cancellation));

        Assert.StartsWith("The mod.io mod has no file for Windows to download.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModsLiveFileForWindowsIsNamedWhenItsFileIsntGiven()
    {
        _harness.Api.Answer("/v1/games/6715/mods", answer => answer.WithBody(
            """{"data": [{"id": 4228735, "game_id": 0, "name_id": "better-inventory-ui1", "modfile": {}, "platforms": [{"platform": "Windows", "modfile_live": 7}]}], "result_total": 1}"""));

        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri("https://mod.io/g/baldursgate3/m/better-inventory-ui1"), ModIoHarness.Cancellation);

        Assert.Equal((new ModFileRef(ModIoHarness.Mod, "7"), (string?)null), (found?.File, found?.SuggestedFileName));
    }
}
