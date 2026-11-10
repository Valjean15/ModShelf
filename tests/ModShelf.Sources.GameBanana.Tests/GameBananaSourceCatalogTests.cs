using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// GameBanana's games, a game, a mod, its files and its updates, as recorded.
/// </summary>
public sealed class GameBananaSourceCatalogTests : IDisposable
{
    private static readonly string[] RecordedGamePages = ["games-1", "games-2", "games-with-game", "games-last"];

    private readonly GameBananaHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsEveryGameReadingEveryPageOfTheList()
    {
        _harness.Api.ServeAll();

        IReadOnlyList<GameInfo> games = await _harness.Source.GetGamesAsync(GameBananaHarness.Cancellation);

        // The pages that weren't recorded are answered with no games.
        int recorded = RecordedGamePages.Sum(name => RecordedGameBananaApi.Body(name)["_aRecords"]!.AsArray().Count);
        Assert.Equal(97, _harness.Asked.Count(asked => asked.StartsWith("/apiv11/Game/Index?", StringComparison.Ordinal)));
        Assert.Equal(recorded, games.Count);
        GameInfo persona = Assert.Single(games, game => game.Game == GameBananaHarness.Game);
        Assert.Equal(("Persona 5 Royal (PC)", new Uri("https://gamebanana.com/games/16951")), (persona.Name, persona.PageUrl));
        Assert.NotNull(persona.CoverUrl);
    }

    [Fact]
    public async Task AGameWithoutPicturesHasNone()
    {
        _harness.Api.ServeAll();

        IReadOnlyList<GameInfo> games = await _harness.Source.GetGamesAsync(GameBananaHarness.Cancellation);

        GameInfo gnorp = Assert.Single(games, game => game.Game.Key == "22680");
        Assert.Equal(((Uri?)null, (Uri?)null), (gnorp.CoverUrl, gnorp.IconUrl));
    }

    [Theory]
    [InlineData("16951", true)]
    [InlineData("fallout4", false)]
    [InlineData("", false)]
    public void ItsGamesKeysAreNumbers(string key, bool could)
    {
        Assert.Equal(could, _harness.Source.CouldBeGameKey(key));
    }

    [Fact]
    public async Task DescribesAGameWithItsPicturesAndTopCategories()
    {
        _harness.Api.Serve("game");

        GameInfo game = await _harness.Source.GetGameAsync(GameBananaHarness.Game, GameBananaHarness.Cancellation);

        Assert.Equal(("Persona 5 Royal (PC)", new Uri("https://gamebanana.com/games/16951")), (game.Name, game.PageUrl));
        Assert.Equal(new Uri("https://images.gamebanana.com/img/banners/games/6351756090c49.jpg"), game.CoverUrl);
        Assert.Equal(new Uri("https://images.gamebanana.com/img/ico/games/63a8d2a8d7a82.png"), game.IconUrl);
        Assert.Equal(
            ["Models", "Other/Misc", "Textures", "QOL", "Events", "Skill Change", "Battles", "Cutscenes / FMV", "Cheats", "Save Files", "Fixes"],
            game.Categories);
    }

    [Theory]
    [InlineData("game-not-found")]
    [InlineData("game-not-found-kept")]
    public async Task AGameThatDoesntExistIsNotFound(string recording)
    {
        _harness.Api.Serve(recording);
        string game = RecordedGameBananaApi.Recording(recording)["request"]!["path"]!.GetValue<string>().Split('/')[3];

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.GetGameAsync(new GameRef(GameBananaHarness.GameBanana, game), GameBananaHarness.Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task DescribesAMod()
    {
        _harness.Api.Serve("mod");

        ModInfo mod = await _harness.Source.GetModAsync(GameBananaHarness.Mod, GameBananaHarness.Cancellation);

        Assert.Equal(
            ("P5R Female Protagonist", "Play as a female protagonist in the Metaverse and overworld.", "chlorophylls", "1.2.28", "Other/Misc"),
            (mod.Name, mod.Summary, mod.Author, mod.Version, mod.Category));
        Assert.Equal((433L, 1224413L, true, false), (mod.Endorsements, mod.Downloads, mod.IsAvailable, mod.HasAdultContent));
        Assert.Equal(
            (DateTimeOffset.FromUnixTimeSeconds(1682722918), DateTimeOffset.FromUnixTimeSeconds(1793598816)),
            (mod.PublishedAt, mod.UpdatedAt));
        Assert.Equal(new Uri("https://gamebanana.com/mods/440088"), mod.PageUrl);
        Assert.Equal(new Uri("https://images.gamebanana.com/img/ss/mods/530-90_69c9e12c2687c.jpg"), mod.PictureUrl);
        Assert.Equal(new Uri("https://images.gamebanana.com/img/ss/mods/220-90_69c9e12c2687c.jpg"), mod.ThumbnailUrl);
    }

    [Fact]
    public async Task AModGameBananaWarnsOfIsForAdults()
    {
        _harness.Api.Serve("mod-rated");
        ModRef rated = new(GameBananaHarness.Game, "413411");

        ModInfo mod = await _harness.Source.GetModAsync(rated, GameBananaHarness.Cancellation);

        Assert.True(mod.HasAdultContent);
    }

    [Fact]
    public async Task ListsAModsFilesSideBySideTheArchivedOneReplacedByItsNewerVersion()
    {
        _harness.Api.Serve("mod");

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(GameBananaHarness.Mod, GameBananaHarness.Cancellation);

        Assert.True(_harness.Source.FilesSideBySide);
        Assert.Equal(
            [
                ("1722096", ModFileCategory.Main, "1.2.28", null),
                ("1722099", ModFileCategory.Miscellaneous, "1.2.28", null),
                ("1662075", ModFileCategory.Main, "1.1.11", null),
                ("1662077", ModFileCategory.Miscellaneous, "1.1.11", null),
                ("1662078", ModFileCategory.Main, "1.1.8", null),
                ("1662079", ModFileCategory.Miscellaneous, "1.1.8", null),
                ("1662070", ModFileCategory.OldVersion, "1.2.27", "1722096"),
            ],
            files.Select(file => (file.Reference.Id, file.Category, file.VersionLabel, file.ReplacedBy?.Id)));
        ModFile main = files[0];
        Assert.Equal(("Main FeMC mod, DOWNLOAD THIS ONE", 218808279L), (main.Name, main.SizeInBytes));
        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, "29eb8bbfbb91ca848fb84d61d355d493"), Assert.Single(main.Hashes));
    }

    [Fact]
    public async Task ReadsAModsUpdatesAsItsChangelogsNewestFirst()
    {
        _harness.Api.Serve("mod-updates");

        IReadOnlyList<ModChangelog> changelogs = await _harness.Source.GetChangelogsAsync(GameBananaHarness.Mod, GameBananaHarness.Cancellation);

        Assert.Equal(44, changelogs.Count);
        Assert.Equal(("1.2.28", "Adjusted Iwai (Hanged Man) Confidant events, including animations"), (changelogs[0].Version, changelogs[0].Changes[0]));
        Assert.Equal("1.1.11", changelogs[1].Version);

        // Named as well as numbered: the version comes first.
        Assert.Equal("1.2.27", changelogs[3].Version);
    }

    [Fact]
    public async Task AnUpdateWithoutAVersionIsNamedByItsTitle()
    {
        _harness.Api.Answer(
            "/apiv11/Mod/440088/Updates",
            answer => answer.WithBody("""{"_aMetadata": {"_nRecordCount": 1, "_bIsComplete": true}, "_aRecords": [{"_sName": "Hotfix", "_aChangeLog": [{"text": " Fixed it. ", "cat": "Bugfix"}, {"text": ""}]}]}"""));

        ModChangelog changelog = Assert.Single(await _harness.Source.GetChangelogsAsync(GameBananaHarness.Mod, GameBananaHarness.Cancellation));

        Assert.Equal("Hotfix", changelog.Version);
        Assert.Equal(["Fixed it."], changelog.Changes);
    }
}
