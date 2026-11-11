using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

public sealed class DegModsSourceCatalogTests : IDisposable
{
    private static readonly SourceId DegMods = new("degmods");
    private static readonly GameRef FalloutNewVegas = new(DegMods, "fallout: new vegas");

    private readonly RecordedDegMods _degMods = new();
    private readonly ServiceProvider _services;

    public DegModsSourceCatalogTests()
    {
        _services = _degMods.BuildServices();
    }

    [Fact]
    public void IsDegModsWithNoWordsForFollowingOrApproval()
    {
        DegModsSource source = Source();

        Assert.Equal("DEG Mods", source.DisplayName);
        Assert.Equal((null, null, null, null), (source.Words.Followed, source.Words.Follow, source.Words.Approval, source.Words.Approvals));
        Assert.False(((IModCatalog)source).FilesSideBySide);
    }

    [Fact]
    public async Task ListsTheGamesItShowsModsOfByTheirNamesLowercase()
    {
        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(TestContext.Current.CancellationToken);

        GameInfo falloutNewVegas = Assert.Single(games, game => game.Game == FalloutNewVegas);
        Assert.Equal("Fallout: New Vegas", falloutNewVegas.Name);
        Assert.Equal("https://degmods.com/game/Fallout%3A%20New%20Vegas", falloutNewVegas.PageUrl?.AbsoluteUri);
        Assert.Contains(games, game => game.Game.Key == "starfield");
        Assert.Contains(games, game => game.Game.Key == "street fighter 6");

        // Its one mod is blocked.
        Assert.DoesNotContain(games, game => game.Game.Key == "pirates, vikings, & knights ii");
    }

    [Fact]
    public async Task NamesAGameAsMostOfItsModsDo()
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("a", "A", "Some GAME", 100, []));
        _degMods.Brs.Add(author.Mod("b", "B", "Some Game", 100, []));
        _degMods.Brs.Add(author.Mod("c", "C", "Some Game", 100, []));
        _degMods.Brs.Add(author.Mod("d", "D", "Other", 100, []));
        _degMods.Brs.Add(author.Mod("e", "E", "OTHER", 100, []));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Some Game", Assert.Single(games, game => game.Game.Key == "some game").Name);
        Assert.Equal("OTHER", Assert.Single(games, game => game.Game.Key == "other").Name);
    }

    [Fact]
    public async Task DescribesAGameItShowsModsOf()
    {
        GameInfo game = await Source().GetGameAsync(FalloutNewVegas, TestContext.Current.CancellationToken);

        Assert.Equal((FalloutNewVegas, "Fallout: New Vegas"), (game.Game, game.Name));
    }

    [Fact]
    public async Task AGameItShowsNoModOfIsNotFound()
    {
        HttpRequestException missing = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source().GetGameAsync(new GameRef(DegMods, "no such game"), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DescribesAModAsItsNewestEventSaysIt()
    {
        ModRef newspapers = new(FalloutNewVegas, $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}");

        ModInfo mod = await Source().GetModAsync(newspapers, TestContext.Current.CancellationToken);

        Assert.Equal(newspapers, mod.Mod);
        Assert.Equal("Better Newspapers for New Vegas Bounties II", mod.Name);
        Assert.Equal(Nip19.Npub(RecordedDegMods.FalloutAuthor), mod.Author);
        Assert.Equal("1.0", mod.Version);
        Assert.Equal(DegModsLink.ModPage(DegModsOptions.DefaultSiteAddress, DegModsLink.ModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers), mod.PageUrl);
        Assert.NotNull(mod.PictureUrl);
        Assert.Equal(mod.PictureUrl, mod.ThumbnailUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788744816), mod.UpdatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788744812), mod.PublishedAt);
        Assert.True(mod.IsAvailable);
        Assert.False(mod.HasAdultContent);
        Assert.Equal((null, null, null), (mod.Downloads, mod.Endorsements, mod.Category));
    }

    [Fact]
    public async Task DescribesAnOlderListingByItsOwnPage()
    {
        ModRef fed = new(new GameRef(DegMods, "starfield"), $"{RecordedDegMods.VgbAuthor}:{RecordedDegMods.FedAndHydration}");

        ModInfo mod = await Source().GetModAsync(fed, TestContext.Current.CancellationToken);

        Assert.Equal(DegModsLink.ModPage(DegModsOptions.DefaultSiteAddress, DegModsLink.OldModKind, RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration), mod.PageUrl);
    }

    [Fact]
    public async Task AModTheAdminMarkedForAdultsIsForAdults()
    {
        TestAuthor admin = new(9);
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("x", "A mod", "Game", 100, []));
        _degMods.Main.Add(admin.Sign(30985, 100, [["d", $"31142:{author.PublicKey}:x"], ["L", "moderation"], ["content-warning", "nsfw"]]));
        using ServiceProvider services = _degMods.BuildServices(options => options with { Admin = admin.PublicKey });

        ModInfo mod = await Source(services).GetModAsync(new ModRef(new GameRef(DegMods, "game"), $"{author.PublicKey}:x"), TestContext.Current.CancellationToken);

        Assert.True(mod.HasAdultContent);
    }

    [Fact]
    public async Task AModTheAdminBlocksIsntShown()
    {
        ModRef blocked = new(new GameRef(DegMods, "pirates, vikings, & knights ii"), $"{RecordedDegMods.TestPostAuthor}:{RecordedDegMods.BlockedPost}");

        HttpRequestException hidden = await Assert.ThrowsAsync<HttpRequestException>(() => Source().GetModAsync(blocked, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Contains("moderation hides", hidden.Message, StringComparison.Ordinal);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("its moderation hides was asked for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AModWithATagTheAdminHidesIsntShown()
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("x", "A mod", "Game", 100, [], ["t", "Gore"]));

        HttpRequestException hidden = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source().GetFilesAsync(new ModRef(new GameRef(DegMods, "game"), $"{author.PublicKey}:x"), TestContext.Current.CancellationToken));

        Assert.Contains("moderation hides", hidden.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModThatIsntThereOrIsUnderAnotherGameIsNotFound()
    {
        DegModsSource source = Source();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        HttpRequestException missing = await Assert.ThrowsAsync<HttpRequestException>(
            () => source.GetModAsync(new ModRef(FalloutNewVegas, $"{RecordedDegMods.FalloutAuthor}:no-such-mod"), cancellationToken));
        HttpRequestException elsewhere = await Assert.ThrowsAsync<HttpRequestException>(
            () => source.GetModAsync(new ModRef(new GameRef(DegMods, "starfield"), $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}"), cancellationToken));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (missing.StatusCode, elsewhere.StatusCode));
        Assert.Contains("another game", elsewhere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsAModsFiles()
    {
        ModRef newspapers = new(FalloutNewVegas, $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}");

        IReadOnlyList<ModFile> files = await Source().GetFilesAsync(newspapers, TestContext.Current.CancellationToken);

        Assert.Equal(3, files.Count);
        Assert.Equal(RecordedDegMods.NewspapersFile[..16], files[0].Reference.Id);
        Assert.Equal(ModFileCategory.Main, files[0].Category);
    }

    [Fact]
    public async Task KeepsNoChangelogs()
    {
        ModRef newspapers = new(FalloutNewVegas, $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}");

        Assert.Empty(await Source().GetChangelogsAsync(newspapers, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1")]
    [InlineData("1234")]
    public async Task RefusesAModThatIsntAnAuthorsAndADTag(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetModAsync(new ModRef(FalloutNewVegas, id), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetChangelogsAsync(new ModRef(FalloutNewVegas, id), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("fallout: new vegas", true)]
    [InlineData("street fighter 6", true)]
    [InlineData("marvel’s spider-man remastered", true)]
    [InlineData("Fallout: New Vegas", false)]
    [InlineData("Starfield", false)]
    [InlineData("starfield", false)]
    [InlineData("stardew-valley", false)]
    [InlineData("fallout4", false)]
    [InlineData("16951", false)]
    [InlineData("", false)]
    public void AKeyAloneIsDegModsWhenItCouldntBeAnotherSites(string key, bool could)
    {
        Assert.Equal(could, Source().CouldBeGameKey(key));
    }

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    private DegModsSource Source() => Source(_services);

    private static DegModsSource Source(ServiceProvider services) => services.GetServices<IModSource>().OfType<DegModsSource>().Single();
}
