using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Thunderstore.Tests.Common;

using static ModShelf.Sources.Thunderstore.Tests.Common.ThunderstoreHarness;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// The communities as games, and each package, its versions as files, and its changelog, from the community's listing.
/// </summary>
public sealed class ThunderstoreSourceCatalogTests : IDisposable
{
    private static readonly DateTimeOffset January = TestPackage.January;

    private readonly ThunderstoreHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheCommunitiesAsGamesWithTheirPicturesOnce()
    {
        _harness.Server.ServeCommunities(("hades-ii", "Hades 2"), ("repo", "R.E.P.O."), ("Bad_Key", "Bad"), ("repo", "Again"));

        IReadOnlyList<GameInfo> games = await _harness.Source.GetGamesAsync(Cancellation);
        await _harness.Source.GetGamesAsync(Cancellation);

        Assert.Equal([("hades-ii", "Hades 2"), ("repo", "R.E.P.O.")], games.Select(game => (game.Game.Key, game.Name)));
        Assert.Equal("https://thunderstore.io/c/hades-ii/", games[0].PageUrl?.AbsoluteUri);
        Assert.Equal("https://ccdn.thunderstore.io/assets/hades-ii/hades-ii-bg-1920x1080.webp", games[0].CoverUrl?.AbsoluteUri);
        Assert.Equal("https://ccdn.thunderstore.io/assets/hades-ii/hades-ii-icon-192x192.webp", games[0].IconUrl?.AbsoluteUri);
        Assert.Single(_harness.Server.Asked);
    }

    [Fact]
    public async Task TakesTheBannerOverTheBackgroundForAGamesCover()
    {
        _harness.Server.ServeCommunities(banners: true, ("hades-ii", "Hades 2"));

        GameInfo game = Assert.Single(await _harness.Source.GetGamesAsync(Cancellation));

        Assert.Equal("https://ccdn.thunderstore.io/assets/hades-ii/hades-ii-bg-1920x620.webp", game.CoverUrl?.AbsoluteUri);
    }

    [Fact]
    public async Task DescribesAGameWithItsCategories()
    {
        _harness.Server.ServeCommunities((Community, "The Game"));
        _harness.Server.ServeCategories(Community, "Mods", "Cosmetics", "AI Generated");

        GameInfo game = await _harness.Source.GetGameAsync(Game, Cancellation);

        Assert.Equal("The Game", game.Name);
        Assert.Equal(["Mods", "Cosmetics", "AI Generated"], game.Categories);
    }

    [Fact]
    public async Task SaysThereIsNoSuchCommunity()
    {
        _harness.Server.ServeCommunities(("repo", "R.E.P.O."));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.GetGameAsync(Game, Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
    }

    [Fact]
    public async Task ReadsTheCommunitiesAgainAfterAFailure()
    {
        _harness.Server.Answer("/api/cyberstorm/community/", HttpStatusCode.BadGateway);
        await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.GetGamesAsync(Cancellation));
        _harness.Server.Reset();
        _harness.Server.ServeCommunities((Community, "The Game"));

        Assert.Single(await _harness.Source.GetGamesAsync(Cancellation));
    }

    [Fact]
    public async Task DescribesAPackageAsAMod()
    {
        _harness.List(new TestPackage("Jowday", "Damage_Meter")
        {
            Categories = ["Tools", "Mods"],
            Created = January.AddDays(-30),
            Updated = January.AddDays(2),
            Likes = 41,
            Adult = true,
            Description = "Shows the damage dealt.",
            Versions = [new("1.2.0", January.AddDays(2), 4096, 100), new("1.1.0", January, 2048, 50)],
        });

        ModInfo mod = await _harness.Source.GetModAsync(Mod("Jowday-Damage_Meter"), Cancellation);

        Assert.Equal(
            ("Damage Meter", "Jowday", "1.2.0", "Shows the damage dealt.", "Tools"),
            (mod.Name, mod.Author, mod.Version, mod.Summary, mod.Category));
        Assert.Equal((150L, 41L, 4096L, true, true), (mod.Downloads, mod.Endorsements, mod.Size, mod.HasAdultContent, mod.IsAvailable));
        Assert.Equal((January.AddDays(2), January.AddDays(-30)), (mod.UpdatedAt, mod.PublishedAt));
        Assert.Equal("https://thunderstore.io/c/game/p/Jowday/Damage_Meter/", mod.PageUrl?.AbsoluteUri);
        Assert.Equal("https://ccdn.thunderstore.io/live/repository/icons/Jowday-Damage_Meter-1.2.0.png", mod.PictureUrl?.AbsoluteUri);
        Assert.Equal(mod.PictureUrl, mod.ThumbnailUrl);
    }

    [Fact]
    public async Task SaysAPackageTheCommunityDoesntListIsGone()
    {
        _harness.List(new TestPackage("Team", "Other"));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.GetModAsync(Mod("Team-Removed"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
    }

    [Fact]
    public async Task ListsEachVersionAsAFileTheNewestTheMainOne()
    {
        _harness.List(new TestPackage("F-tank", "Pack")
        {
            Versions =
            [
                new("2.0.0", January.AddDays(3), 3000),
                new("1.1.0", January.AddDays(1), 2000),

                // Numbered lower, but uploaded after 1.1.0, as a fix to an older line.
                new("1.0.1", January.AddDays(2), 1500),
                new("1.0.0", January, 1000),
            ],
        });

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(Mod("F-tank-Pack"), Cancellation);

        Assert.Equal(
            [
                ("2.0.0", ModFileCategory.Main, (string?)null),
                ("1.1.0", ModFileCategory.OldVersion, "2.0.0"),
                ("1.0.1", ModFileCategory.OldVersion, null),
                ("1.0.0", ModFileCategory.OldVersion, "1.0.1"),
            ],
            files.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
        ModFile newest = files[0];
        Assert.Equal(("Pack 2.0.0", "F-tank-Pack-2.0.0.zip", "2.0.0", 3000L, January.AddDays(3)), (newest.Name, newest.FileName, newest.VersionLabel, newest.SizeInBytes, newest.UploadedAt));
        Assert.Equal("https://thunderstore.io/c/game/p/F-tank/Pack/v/2.0.0/", newest.PageUrl?.AbsoluteUri);
        Assert.Empty(newest.Hashes);
    }

    [Fact]
    public async Task ReadsTheNewestVersionsChangelog()
    {
        _harness.List(new TestPackage("Team", "Mod") { Versions = [new("1.1.0", January.AddDays(1)), new("1.0.0", January)] });
        _harness.Server.ServeChangelog("Team", "Mod", "1.1.0", "## 1.1.0\n- Faster.\n## 1.0.0\n- First.");

        IReadOnlyList<ModChangelog> changelogs = await _harness.Source.GetChangelogsAsync(Mod("Team-Mod"), Cancellation);

        Assert.Equal([("1.1.0", "Faster."), ("1.0.0", "First.")], changelogs.Select(changelog => (changelog.Version, Assert.Single(changelog.Changes))));
    }

    [Fact]
    public async Task ReadsNoChangelogWhenThereIsNone()
    {
        _harness.List(new TestPackage("Team", "Mod"), new TestPackage("Team", "Gone"));
        _harness.Server.ServeChangelog("Team", "Mod", "1.0.0", null);
        _harness.Server.Answer("/api/experimental/package/Team/Gone/1.0.0/changelog/", HttpStatusCode.NotFound);

        Assert.Empty(await _harness.Source.GetChangelogsAsync(Mod("Team-Mod"), Cancellation));
        Assert.Empty(await _harness.Source.GetChangelogsAsync(Mod("Team-Gone"), Cancellation));
    }

    [Theory]
    [InlineData("lethal-company")]
    [InlineData("repo")]
    [InlineData("16951")]
    public void TakesNoKeyAloneAsOneOfItsGames(string key)
    {
        // A key alone stays Nexus Mods', whose keys look the same (ADR 0103).
        Assert.False(_harness.Source.CouldBeGameKey(key));
    }

    [Fact]
    public async Task RefusesAnotherSitesModOrAModThatIsntAPackage()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.GetModAsync(new ModRef(new GameRef(new SourceId("nexus"), "game"), "Team-Mod"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.GetModAsync(Mod("12345"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.GetFilesAsync(new ModRef(new GameRef(ThunderstoreHarness.Thunderstore, "Not A Key"), "Team-Mod"), Cancellation));
    }
}
