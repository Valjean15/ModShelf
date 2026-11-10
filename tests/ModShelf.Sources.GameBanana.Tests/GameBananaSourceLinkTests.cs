using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// Which file a link to GameBanana points to, asked of the mod's profile, which names its game.
/// </summary>
public sealed class GameBananaSourceLinkTests : IDisposable
{
    private readonly GameBananaHarness _harness = new();

    public GameBananaSourceLinkTests()
    {
        _harness.Api.Serve("mod");
    }

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("https://gamebanana.com/mods/440088")]
    [InlineData("https://gamebanana.com/mods/download/440088")]
    public async Task AModsLinkIsItsArchiveUploadedLastOfItsGame(string link)
    {
        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri(link), GameBananaHarness.Cancellation);

        Assert.Equal(new ModFileRef(GameBananaHarness.Mod, RecordedGameBananaApi.MainFile), found?.File);
        Assert.Equal("p5r_female_protagonist1_2_28.7z", found?.SuggestedFileName);
        Assert.Equal((false, false), (found?.IsCatchAll, found?.IsHandoff));
        Assert.Equal(["/apiv11/Mod/440088/ProfilePage"], _harness.Asked);
    }

    [Theory]
    [InlineData("https://gamebanana.com/mmdl/1662075,Mod,440088", "1662075", "p5r_female_protagonist_-_english_voiceover1_1_11.7z")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1662070,Mod,440088", "1662070", "p5r_female_protagonist1_2_27.7z")]
    public async Task AOneClickLinkIsTheFileItNamesCurrentOrArchived(string link, string file, string fileName)
    {
        RecognizedLink? found = await _harness.Source.RecognizeAsync(new Uri(link), GameBananaHarness.Cancellation);

        Assert.Equal((new ModFileRef(GameBananaHarness.Mod, file), fileName), (found?.File, found?.SuggestedFileName));
    }

    [Fact]
    public async Task AFileTheModDoesntHaveIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.RecognizeAsync(
            new Uri("https://gamebanana.com/mmdl/999,Mod,440088"), GameBananaHarness.Cancellation));
    }

    [Theory]
    [InlineData("https://gamebanana.com/dl/1722096")]
    [InlineData("https://gamebanana.com/games/16951")]
    public async Task ALinkThatNamesNoModIsRefusedWithoutAskingGameBanana(string link)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.RecognizeAsync(new Uri(link), GameBananaHarness.Cancellation));

        Assert.Empty(_harness.Asked);
    }

    [Fact]
    public async Task ALinkOfAnotherSiteIsntGameBananas()
    {
        Assert.Null(await _harness.Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), GameBananaHarness.Cancellation));
        Assert.Empty(_harness.Asked);
    }

    [Theory]
    [InlineData("mod-not-found")]
    [InlineData("mod-not-found-kept")]
    public async Task AModThatDoesntExistIsNotFoundWhateverStatusGameBananaGives(string recording)
    {
        _harness.Api.Serve(recording);
        string path = RecordedGameBananaApi.Recording(recording)["request"]!["path"]!.GetValue<string>();
        string mod = path.Split('/')[3];

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.RecognizeAsync(new Uri($"https://gamebanana.com/mods/{mod}"), GameBananaHarness.Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains("This Mod doesn't exist", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModWithoutFilesIsRefused()
    {
        _harness.Api.Answer("/apiv11/Mod/1/ProfilePage", answer => answer.WithBody("""{"_idRow": 1, "_sName": "Empty", "_aGame": {"_idRow": 16951}, "_aFiles": []}"""));

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.RecognizeAsync(new Uri("https://gamebanana.com/mods/1"), GameBananaHarness.Cancellation));
    }
}
