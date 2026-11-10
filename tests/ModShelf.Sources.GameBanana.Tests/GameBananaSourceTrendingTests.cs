using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// The mods a game's page shows as its top submissions of late.
/// </summary>
public sealed class GameBananaSourceTrendingTests : IDisposable
{
    private readonly GameBananaHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TheGamesTopModsEachOnceWithoutItsOtherSubmissions()
    {
        _harness.Api.Serve("game-trending");

        IReadOnlyList<ModInfo> trending = await _harness.Source.GetTrendingAsync(GameBananaHarness.Game, GameBananaHarness.Cancellation);

        string[] mods =
        [
            .. RecordedGameBananaApi.Body("game-trending").AsArray()
                .Where(top => top!["_sModelName"]!.GetValue<string>() == "Mod")
                .Select(top => top!["_idRow"]!.ToString())
                .Distinct(),
        ];
        Assert.Equal(mods, trending.Select(mod => mod.Mod.Id));
        Assert.Equal(("737365", "Genji Yusuke"), (trending[0].Mod.Id, trending[0].Name));
        Assert.Equal(new Uri("https://images.gamebanana.com/img/ss/mods/220-90_6aeeeed71488c.jpg"), trending[0].ThumbnailUrl);
    }

    [Fact]
    public async Task MarksTheTopModsForAdults()
    {
        _harness.Api.Answer(
            "/apiv11/Game/16951/TopSubs",
            answer => answer.WithBody("""[{"_idRow": 1, "_sModelName": "Mod", "_sName": "Rated", "_sInitialVisibility": "hide"}, {"_idRow": 1, "_sModelName": "Mod", "_sName": "Rated"}]"""));

        ModInfo rated = Assert.Single(await _harness.Source.GetTrendingAsync(GameBananaHarness.Game, GameBananaHarness.Cancellation));

        Assert.True(rated.HasAdultContent);
    }
}
