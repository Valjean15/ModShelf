using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// GameBanana's games found by name: by the longest word typed, 50 a page, two pages at most, each word's answer kept.
/// </summary>
public sealed class GameBananaSourceGameSearchTests : IDisposable
{
    private readonly GameBananaHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("Persona 5 Royal (PC)", "persona")]
    [InlineData("final fantasy 12", "fantasy")]
    [InlineData("Dragon's Dogma", "dragon")]
    [InlineData("abc xyz", "abc")]
    [InlineData("S.T.A.L.K.E.R.", null)]
    [InlineData("p5 r", null)]
    [InlineData("", null)]
    public void AsksForTheLongestWordTypedOfThreeLettersOrMore(string words, string? asked)
    {
        Assert.Equal(asked, GameBananaSource.LongestWord(words));
    }

    [Fact]
    public async Task FindsTheGamesWhoseNamesHaveTheLongestWordWithTheirPictures()
    {
        _harness.Api.Serve("games-find");

        IReadOnlyList<GameInfo> found = await _harness.Source.FindGamesAsync("Persona 5 Royal", GameBananaHarness.Cancellation);

        Assert.Equal(RecordedGameBananaApi.Body("games-find")["_aRecords"]!.AsArray().Count, found.Count);
        GameInfo royal = Assert.Single(found, game => game.Game == GameBananaHarness.Game);
        Assert.Equal(
            ("Persona 5 Royal (PC)", new Uri("https://gamebanana.com/games/16951"), new Uri("https://images.gamebanana.com/img/banners/games/6351756090c49.jpg"), new Uri("https://images.gamebanana.com/img/ico/games/63a8d2a8d7a82.png")),
            (royal.Name, royal.PageUrl, royal.CoverUrl, royal.IconUrl));
        Assert.Contains(found, game => game.CoverUrl is null || game.IconUrl is null);
        Assert.All(found, game => Assert.False(game.CoverUrl?.AbsolutePath.EndsWith("/_.gif", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public async Task KeepsEachWordsAnswer()
    {
        _harness.Api.Serve("games-find");

        IReadOnlyList<GameInfo> first = await _harness.Source.FindGamesAsync("persona", GameBananaHarness.Cancellation);
        IReadOnlyList<GameInfo> again = await _harness.Source.FindGamesAsync("Persona 5", GameBananaHarness.Cancellation);

        Assert.Same(first, again);
        Assert.Single(_harness.Api.Requests);
    }

    [Fact]
    public async Task ReadsTwoPagesAtMost()
    {
        _harness.Api.Answer("/apiv11/Util/Game/NameMatch", "_nPage", "1", answer => answer.WithBody(Page(false, 1, 2)));
        _harness.Api.Answer("/apiv11/Util/Game/NameMatch", "_nPage", "2", answer => answer.WithBody(Page(false, 2, 3)));

        IReadOnlyList<GameInfo> found = await _harness.Source.FindGamesAsync("the", GameBananaHarness.Cancellation);

        Assert.Equal(["1", "2", "3"], found.Select(game => game.Game.Key));
        Assert.Equal(2, _harness.Api.Requests.Count);
    }

    [Fact]
    public async Task AWordTooShortIsRefusedWithoutAsking()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.FindGamesAsync("p5", GameBananaHarness.Cancellation));

        Assert.StartsWith("GameBanana finds games by a word of 3 letters or more.", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task ARefusalIsntKept()
    {
        _harness.Api.Answer("/apiv11/Util/Game/NameMatch", answer => answer.WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("<html>Down</html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.FindGamesAsync("persona", GameBananaHarness.Cancellation));
        _harness.Api.Reset();
        _harness.Api.Serve("games-find");
        IReadOnlyList<GameInfo> found = await _harness.Source.FindGamesAsync("persona", GameBananaHarness.Cancellation);

        Assert.NotEmpty(found);
    }

    [Fact]
    public async Task KeepsTheAnswersOfAsManyWordsAsItKeepsThenAsksAgain()
    {
        _harness.Api.Answer("/apiv11/Util/Game/NameMatch", answer => answer.WithBody(Page(true, 1)));

        for (int word = 0; word < GameBananaSource.FoundKept; word++)
        {
            await _harness.Source.FindGamesAsync($"word{word}", GameBananaHarness.Cancellation);
        }

        await _harness.Source.FindGamesAsync("word0", GameBananaHarness.Cancellation);
        await _harness.Source.FindGamesAsync("another", GameBananaHarness.Cancellation);
        await _harness.Source.FindGamesAsync("another", GameBananaHarness.Cancellation);

        Assert.Equal(GameBananaSource.FoundKept + 2, _harness.Api.Requests.Count);
    }

    [Fact]
    public async Task LeavesOutAGameWithoutAName()
    {
        _harness.Api.Answer(
            "/apiv11/Util/Game/NameMatch",
            answer => answer.WithBody("""{"_aMetadata": {"_bIsComplete": true}, "_aRecords": [{"_idRow": 1, "_sName": " "}, {"_idRow": 2}, {"_idRow": 3, "_sName": "Game 3"}]}"""));

        IReadOnlyList<GameInfo> found = await _harness.Source.FindGamesAsync("game", GameBananaHarness.Cancellation);

        Assert.Equal(["3"], found.Select(game => game.Game.Key));
    }

    /// <summary>
    /// A page of games found by name, with the IDs given, numbered by them.
    /// </summary>
    private static string Page(bool complete, params int[] ids)
        => $$"""{"_aMetadata": {"_nRecordCount": 120, "_nPerpage": 50, "_bIsComplete": {{(complete ? "true" : "false")}}}, "_aRecords": [{{string.Join(", ", ids.Select(id => $$"""{"_idRow": {{id}}, "_sName": "Game {{id}}"}"""))}}]}""";
}
