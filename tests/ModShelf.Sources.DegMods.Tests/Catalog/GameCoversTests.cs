using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Catalog;

/// <summary>
/// DEG Mods' games' covers, from the games database its admin publishes (ADR 0093). The database's event is signed here
/// by a test admin, and its files are made here: DEG Mods' own are about 28 MB.
/// </summary>
public sealed class GameCoversTests : IDisposable
{
    private const string Header = "Game Name,16 by 9 image,Boxart image";

    private static readonly SourceId DegMods = new("degmods");

    private readonly RecordedDegMods _degMods = new();
    private readonly TestAuthor _admin = new(9);
    private readonly ServiceProvider _services;

    public GameCoversTests()
    {
        _services = _degMods.BuildServices(options => options with { Admin = _admin.PublicKey });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    [Fact]
    public async Task EachGameHasItsWidePictureOrElseItsBoxArtALaterFilesRowWinning()
    {
        string steam = Csv(
            "Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,https://cdn.example/fnv/library_600x900.jpg",
            "Street Fighter 6,https://cdn.example/sf6/old_hero.jpg,",
            "\"Starfield\",,https://cdn.example/starfield/library_600x900.jpg",
            "A Game Without Mods,https://cdn.example/none/library_hero.jpg,");
        string modded = Csv("STREET FIGHTER 6,https://cdn.example/sf6/library_hero.jpg,");
        PublishDatabase(steam, modded);

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
        Assert.Equal("https://cdn.example/sf6/library_hero.jpg", Cover(games, "street fighter 6"));
        Assert.Equal("https://cdn.example/starfield/library_600x900.jpg", Cover(games, "starfield"));
        Assert.Contains(games, game => game.CoverUrl is null);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("DEG Mods' games database has covers for 3 of its", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsTheDatabaseOnceARunFromTheFirstServerThatGivesEachFileWhole()
    {
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        string modded = Csv("Starfield,https://cdn.example/starfield/library_hero.jpg,");
        PublishDatabase(steam, modded, onPrimal: false);
        _degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(steam)}"), Encoding.UTF8.GetBytes("Game Name\nTampered,https://evil.example/x.jpg,"));
        _degMods.Serve(new Uri($"https://nostr.download/{Sha256(steam)}"), Encoding.UTF8.GetBytes(steam));
        _degMods.Serve(new Uri($"https://blossom.data.haus/{Sha256(modded)}"), Encoding.UTF8.GetBytes(modded));

        IReadOnlyList<GameInfo> first = await Source().GetGamesAsync(Cancellation);
        int asked = _degMods.Asked.Count;
        GameInfo game = await Source().GetGameAsync(new GameRef(DegMods, "fallout: new vegas"), Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(first, "fallout: new vegas"));
        Assert.Equal("https://cdn.example/starfield/library_hero.jpg", Cover(first, "starfield"));
        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", game.CoverUrl?.AbsoluteUri);
        Assert.Equal(asked, _degMods.Asked.Count);
        Assert.Equal(
            [
                $"GET blossom.primal.net/{Sha256(steam)}",
                $"GET blossom.data.haus/{Sha256(steam)}",
                $"GET nostr.download/{Sha256(steam)}",
            ],
            _degMods.Asked.Where(request => request.EndsWith(Sha256(steam), StringComparison.Ordinal)));
        Assert.DoesNotContain(_degMods.Asked, request => request.Contains("brs.degmods.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FollowsWhereAServerSendsForTheFile()
    {
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        PublishDatabase(steam, onPrimal: false);
        _degMods.Answer("GET", new Uri($"https://blossom.primal.net/{Sha256(steam)}"), 302, new Dictionary<string, string> { ["Location"] = $"https://r2a.primal.net/uploads2/{Sha256(steam)}.bin" });
        _degMods.Serve(new Uri($"https://r2a.primal.net/uploads2/{Sha256(steam)}.bin"), Encoding.UTF8.GetBytes(steam));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
        Assert.DoesNotContain(_degMods.Asked, request => request.Contains("blossom.data.haus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALaterFilesRowWithoutAPictureLeavesTheGameWithout()
    {
        PublishDatabase(
            Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,"),
            Csv("Fallout: New Vegas,,"));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Null(Cover(games, "fallout: new vegas"));
    }

    [Fact]
    public async Task FollowsAServersRedirectWithinIt()
    {
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        PublishDatabase(steam, onPrimal: false);
        _degMods.Answer("GET", new Uri($"https://blossom.primal.net/{Sha256(steam)}"), 302, new Dictionary<string, string> { ["Location"] = $"/uploads2/{Sha256(steam)}.bin" });
        _degMods.Serve(new Uri($"https://blossom.primal.net/uploads2/{Sha256(steam)}.bin"), Encoding.UTF8.GetBytes(steam));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
    }

    [Fact]
    public async Task AFilesHashInCapitalsIsReadByItsHash()
    {
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        _degMods.Main.Add(_admin.Sign(Moderation.ListKind, 1_000, [["d", GameCovers.DatabaseList], ["csv", Sha256(steam).ToUpperInvariant(), "s-games-1"]]));
        _degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(steam)}"), Encoding.UTF8.GetBytes(steam));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
    }

    [Fact]
    public async Task NeverAsksDegModsOwnServers()
    {
        using RecordedDegMods degMods = new();
        using ServiceProvider services = degMods.BuildServices(options => options with
        {
            Admin = _admin.PublicKey,
            FileServers = [new Uri("https://brs.degmods.com/"), new Uri("https://blossom.primal.net/")],
        });
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        degMods.Main.Add(_admin.Sign(Moderation.ListKind, 1_000, [["d", GameCovers.DatabaseList], ["csv", Sha256(steam)]]));
        degMods.Serve(new Uri($"https://brs.degmods.com/{Sha256(steam)}"), Encoding.UTF8.GetBytes(steam));
        degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(steam)}"), Encoding.UTF8.GetBytes(steam));

        IReadOnlyList<GameInfo> games = await ((DegModsSource)services.GetServices<IModSource>().Single()).GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
        Assert.DoesNotContain(degMods.Asked, request => request.Contains("brs.degmods.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutTheDatabaseGamesHaveNoCoverAndItsReadAgainTheNextTime()
    {
        IReadOnlyList<GameInfo> without = await Source().GetGamesAsync(Cancellation);
        PublishDatabase(Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,"));

        IReadOnlyList<GameInfo> with = await Source().GetGamesAsync(Cancellation);

        Assert.All(without, game => Assert.Null(game.CoverUrl));
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("DEG Mods' relays hold no games database, so its games show their initials", StringComparison.Ordinal));
        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(with, "fallout: new vegas"));
    }

    [Fact]
    public async Task AFileNoServerGivesIsPassedOver()
    {
        string steam = Csv("Fallout: New Vegas,https://cdn.example/fnv/library_hero.jpg,");
        string missing = Csv("Starfield,https://cdn.example/starfield/library_hero.jpg,");
        PublishDatabase(steam, missing, onPrimal: false);
        _degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(steam)}"), Encoding.UTF8.GetBytes(steam));
        _degMods.Unreachable.Add("nostr.download");

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/library_hero.jpg", Cover(games, "fallout: new vegas"));
        Assert.Null(Cover(games, "starfield"));
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains($"No file server gave DEG Mods' games database file {Sha256(missing)[..8]}…, so its games are passed over", StringComparison.Ordinal));
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("The file server nostr.download couldn't be asked for a file of DEG Mods' games database", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyTheAdminsNewestDatabaseIsRead()
    {
        string older = Csv("Fallout: New Vegas,https://cdn.example/fnv/older.jpg,");
        string newer = Csv("Fallout: New Vegas,https://cdn.example/fnv/newer.jpg,");
        string someoneElses = Csv("Fallout: New Vegas,https://cdn.example/fnv/someone.jpg,");
        PublishDatabase(older);
        PublishDatabase(newer, createdAt: 2_000);
        _degMods.Main.Add(new TestAuthor(10).Sign(Moderation.ListKind, 3_000, [["d", GameCovers.DatabaseList], ["csv", Sha256(someoneElses)]]));
        _degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(someoneElses)}"), Encoding.UTF8.GetBytes(someoneElses));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal("https://cdn.example/fnv/newer.jpg", Cover(games, "fallout: new vegas"));
    }

    [Theory]
    [InlineData("http://localhost:3000/hero.jpg", "https://cdn.example/fnv/box.jpg")]
    [InlineData("ftp://cdn.example/hero.jpg", "https://cdn.example/fnv/box.jpg")]
    [InlineData("not a link", "https://cdn.example/fnv/box.jpg")]
    [InlineData("http://192.168.1.2/hero.jpg", "http://192.168.1.2/box.jpg")]
    public async Task APictureThatIsntOnTheWebIsPassedOver(string wide, string boxArt)
    {
        PublishDatabase(Csv($"Fallout: New Vegas,{wide},{boxArt}"));

        IReadOnlyList<GameInfo> games = await Source().GetGamesAsync(Cancellation);

        Assert.Equal(boxArt.Contains("192.168", StringComparison.Ordinal) ? null : boxArt, Cover(games, "fallout: new vegas"));
    }

    [Theory]
    [InlineData("a,b,c", new[] { "a", "b", "c" })]
    [InlineData("\"Pirates, Vikings, & Knights II\",,x", new[] { "Pirates, Vikings, & Knights II", "", "x" })]
    [InlineData("\"The \"\"Quoted\"\" Game\",y", new[] { "The \"Quoted\" Game", "y" })]
    [InlineData("", new[] { "" })]
    public void ReadsACsvLinesFields(string line, string[] fields)
    {
        Assert.Equal(fields, GameCovers.Fields(line));
    }

    private static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    private static string Sha256(string file) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file)));

    private static string? Cover(IReadOnlyList<GameInfo> games, string key)
        => games.Single(game => game.Game.Key == key).CoverUrl?.AbsoluteUri;

    /// <summary>
    /// Publishes the admin's games database listing <paramref name="files"/> in order, each served by the first file
    /// server unless said.
    /// </summary>
    private void PublishDatabase(string first, string? second = null, bool onPrimal = true, long createdAt = 1_000)
    {
        string[] files = second is null ? [first] : [first, second];
        _degMods.Main.Add(_admin.Sign(
            Moderation.ListKind,
            createdAt,
            [["d", GameCovers.DatabaseList], .. files.Select((file, index) => new[] { "csv", Sha256(file), $"s-games-{index + 1}" })]));
        if (onPrimal)
        {
            foreach (string file in files)
            {
                _degMods.Serve(new Uri($"https://blossom.primal.net/{Sha256(file)}"), Encoding.UTF8.GetBytes(file));
            }
        }
    }

    private DegModsSource Source() => (DegModsSource)_services.GetServices<IModSource>().Single();
}
