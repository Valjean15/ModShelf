using System.Text.Json.Nodes;

using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Catalog;

public sealed class ModEventsTests
{
    private static readonly TestAuthor Author = new(5);
    private static readonly long Before = ModEvents.OldModsEnd - 86_400;
    private static readonly string Sha = new('a', 64);

    // A mod's name, game and d tag, each of which it must have.
    private static readonly string[][] Named = [["d", "x"], ["title", "A mod"], ["g", "Game"]];

    [Fact]
    public void ReadsANewModAsItsEventSaysIt()
    {
        NostrEvent newspapers = Recorded(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, DegModsLink.ModKind);

        DegMod mod = ModEvents.Read(newspapers)!;

        Assert.Equal((DegModsLink.ModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers), (mod.Kind, mod.Author, mod.D));
        Assert.Equal($"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}", mod.Key);
        Assert.Equal("Better Newspapers for New Vegas Bounties II", mod.Title);
        Assert.Equal("Fallout: New Vegas", mod.Game);
        Assert.NotNull(mod.Summary);
        Assert.NotNull(mod.Picture);
        Assert.Contains("fnv", mod.Tags);
        Assert.False(mod.IsAdult);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788744812), mod.PublishedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788744816), mod.UpdatedAt);
        Assert.Equal(3, mod.Downloads.Count);
        DegDownload first = mod.Downloads[0];
        Assert.Equal(RecordedDegMods.NewspapersFile[..16], first.Id);
        Assert.Equal(RecordedDegMods.NewspapersFile, first.Sha256);
        Assert.Equal("brs.degmods.com", first.Url.Host);
        Assert.Equal(("Better Newspapers for New Vegas Bounties II", "1.0"), (first.Title, first.Version));
        Assert.Equal("Better Newspapers for New Vegas Bounties II-96533-1-0-1771837702.zip", first.FileName);
        Assert.Equal(["1.0", "1.0LE", "1.0ALTTEX"], mod.Downloads.Select(download => download.Version));
    }

    [Fact]
    public void ReadsAnOlderListingAsItsEventSaysIt()
    {
        NostrEvent fed = Recorded(RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration, DegModsLink.OldModKind);

        DegMod mod = ModEvents.Read(fed)!;

        Assert.Equal(DegModsLink.OldModKind, mod.Kind);
        Assert.Equal("VGB Fed and Hydration Duration Increase", mod.Title);
        Assert.Equal("Starfield", mod.Game);
        Assert.Equal(["bethesda", "buff duration", "starfield"], mod.Tags.Order(StringComparer.Ordinal));
        DegDownload file = Assert.Single(mod.Downloads);
        Assert.Equal("https://files.catbox.moe/wa4qcn.7z", file.Url.AbsoluteUri);
        Assert.Null(file.Sha256);
        Assert.Null(file.Title);
        Assert.Equal("1.0", file.Version);
    }

    [Fact]
    public void AModsNewEventWinsOverItsOlderListing()
    {
        IReadOnlyList<DegMod> mods = ModEvents.Read(RecordedDegMods.RecordedEvents("brs.degmods.com").Concat(RecordedDegMods.RecordedEvents("relay.degmods.com")));

        Assert.Equal(DegModsLink.ModKind, mods.Single(mod => mod.D == RecordedDegMods.Nipton).Kind);
        Assert.Equal(mods.Count, mods.Select(mod => mod.Key).Distinct().Count());
    }

    [Fact]
    public void AnOlderListingThatMovedIsntAModEvenWithoutItsNewEvent()
    {
        NostrEvent moved = Author.Sign(DegModsLink.OldModKind, Before, Old("moved", [["legacy", "yes"]]));

        Assert.Empty(ModEvents.Read([moved]));
        Assert.Null(ModEvents.Read(moved));
    }

    [Fact]
    public void AnOlderListingMadeSinceAugustIsntAModAndTheOneBeforeIs()
    {
        NostrEvent before = Author.Sign(DegModsLink.OldModKind, Before, [["d", "x"], ["t", "GameMod"], ["title", "Before"], ["game", "Game"]]);
        NostrEvent since = Author.Sign(DegModsLink.OldModKind, ModEvents.OldModsEnd, [["d", "x"], ["t", "GameMod"], ["title", "Since"], ["game", "Game"]]);

        Assert.Null(ModEvents.Read(since));
        Assert.Equal("Before", Assert.Single(ModEvents.Read([before, since])).Title);
    }

    [Fact]
    public void AnOlderListingWithoutItsGameModTagIsntAMod()
    {
        NostrEvent listing = Author.Sign(DegModsLink.OldModKind, Before, [["d", "x"], ["title", "A car"], ["game", "Game"]]);

        Assert.Null(ModEvents.Read(listing));
    }

    [Fact]
    public void TheNewestEventOfAModWinsTheLowerIdAtTheSameSecond()
    {
        NostrEvent older = Mod("x", "Older", 100);
        NostrEvent newer = Mod("x", "Newer", 200);
        NostrEvent twin = Mod("x", "Twin", 200);
        string lower = string.CompareOrdinal(newer.Id, twin.Id) < 0 ? "Newer" : "Twin";

        Assert.Equal("Newer", Assert.Single(ModEvents.Read([older, newer])).Title);
        Assert.Equal("Newer", Assert.Single(ModEvents.Read([newer, older])).Title);
        Assert.Equal(lower, Assert.Single(ModEvents.Read([newer, twin])).Title);
        Assert.Equal(lower, Assert.Single(ModEvents.Read([twin, newer])).Title);
    }

    [Fact]
    public void AModWhoseNewestEventSaysItsDeletedIsGoneThoughAnOlderListingIsnt()
    {
        NostrEvent old = Author.Sign(DegModsLink.OldModKind, Before, Old("x", []));
        NostrEvent deleted = Mod("x", "Gone", Before + 10, ["deleted", "true"]);

        Assert.Empty(ModEvents.Read([old, deleted]));
    }

    [Fact]
    public void AModsOfDifferentAuthorsWithTheSameDTagAreTwo()
    {
        NostrEvent mine = Mod("same", "Mine", 100);
        NostrEvent theirs = new TestAuthor(6).Mod("same", "Theirs", "Game", 100, []);

        Assert.Equal(2, ModEvents.Read([mine, theirs]).Count);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("g")]
    [InlineData("d")]
    public void AnEventWithoutANameGameOrDTagIsntAMod(string missing)
    {
        NostrEvent mod = Author.Sign(DegModsLink.ModKind, 100, Named.Where(tag => tag[0] != missing));
        NostrEvent blank = Author.Sign(DegModsLink.ModKind, 100, Named.Select(tag => tag[0] == missing ? [missing, "  "] : tag));

        Assert.Null(ModEvents.Read(mod));
        Assert.Null(ModEvents.Read(blank));
    }

    [Fact]
    public void AnEventOfAnotherKindIsntAMod()
    {
        Assert.Null(ModEvents.Read(Author.Sign(31143, 100, [["d", "x"], ["title", "A jam"], ["g", "Game"]])));
    }

    [Fact]
    public void ADTagLongerThanAnAddressHoldsIsntAMod()
    {
        Assert.Null(ModEvents.Read(Mod(new string('x', 256), "Long", 100)));
        Assert.NotNull(ModEvents.Read(Mod(new string('x', 255), "Long", 100)));
    }

    [Theory]
    [InlineData("content-warning", "nsfw")]
    [InlineData("content-warning", "")]
    [InlineData("nsfw", "true")]
    public void AModIsForAdultsWhenItsAuthorSaysSo(string tag, string value)
    {
        Assert.True(ModEvents.Read(Mod("x", "A mod", 100, [tag, value]))!.IsAdult);
    }

    [Fact]
    public void AModIsntForAdultsWhenItsNsfwTagSaysOtherwise()
    {
        Assert.False(ModEvents.Read(Mod("x", "A mod", 100, ["nsfw", "false"]))!.IsAdult);
    }

    [Fact]
    public void ReadsTagsLowercaseEachOnce()
    {
        DegMod mod = ModEvents.Read(Mod("x", "A mod", 100, ["t", " Music "], ["t", "music"], ["t", ""], ["t", "AUDIO"]))!;

        Assert.Equal(["audio", "music"], mod.Tags.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("500", 100)]
    [InlineData("soon", 100)]
    [InlineData("-5", 100)]
    [InlineData("0", 100)]
    public void APublishingTimeIsNeverAfterTheEvent(string published, long expected)
    {
        NostrEvent mod = Author.Sign(DegModsLink.ModKind, 100, [["d", "x"], ["title", "A mod"], ["g", "Game"], ["published_at", published]]);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expected), ModEvents.Read(mod)!.PublishedAt);
    }

    [Fact]
    public void ReadsFilesOnceEachAndLeavesOutWhatIsntOne()
    {
        string sha = new('B', 64);
        NostrEvent mod = Mod(
            "x",
            "A mod",
            100,
            ["download", Json(("file", $"https://brs.degmods.com/{sha}.zip"), ("hash", sha), ("title", "Main"), ("filename", "main.zip"), ("version", "2"))],
            ["download", Json(("file", $"https://blossom.primal.net/{sha}.zip"), ("hash", sha))],
            ["download", Json(("file", "javascript:alert(1)"))],
            ["download", Json(("file", "/relative.zip"))],
            ["download", "not json"],
            ["download", "[1]"],
            ["download", Json(("file", "https://files.catbox.moe/abc.zip"), ("hash", "not a hash"))],
            ["downloads", "file:https://example.com/x.zip", $"hash:{Sha}", "title:Pairs", "version:3", "nonsense"]);

        IReadOnlyList<DegDownload> downloads = ModEvents.Read(mod)!.Downloads;

        Assert.Equal(3, downloads.Count);
        Assert.Equal((sha.ToLowerInvariant()[..16], sha.ToLowerInvariant(), "Main", "main.zip", "2"), (downloads[0].Id, downloads[0].Sha256, downloads[0].Title, downloads[0].FileName, downloads[0].Version));
        Assert.Null(downloads[1].Sha256);
        Assert.Equal(16, downloads[1].Id.Length);
        Assert.Equal(("https://example.com/x.zip", Sha, "Pairs", "3"), (downloads[2].Url.AbsoluteUri, downloads[2].Sha256, downloads[2].Title, downloads[2].Version));
    }

    [Fact]
    public void AFileWithoutAHashIsNamedByItsLinksHash()
    {
        DegDownload file = Assert.Single(ModEvents.Read(Mod("x", "A mod", 100, ["download", Json(("file", "https://files.catbox.moe/abc.zip"))]))!.Downloads);

        // SHA-256 of "https://files.catbox.moe/abc.zip".
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("https://files.catbox.moe/abc.zip"u8))[..16], file.Id);
    }

    [Fact]
    public void ReadsBackAModsKey()
    {
        Assert.True(ModEvents.TryReadKey($"{Author.PublicKey}:some d:with colons", out string author, out string d));
        Assert.Equal((Author.PublicKey, "some d:with colons"), (author, d));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc:def")]
    [InlineData("16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1")]
    [InlineData("16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:")]
    [InlineData("16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1-x")]
    [InlineData("16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1bz:x")]
    [InlineData("16E23DD46D8F413B6A993FF5555841D1AD76DF5E45476ADA84593F5ED4C1E1B1:x")]
    public void SomethingElseIsntAModsKey(string? key)
    {
        Assert.False(ModEvents.TryReadKey(key, out _, out _));
    }

    [Fact]
    public void AKeyWithADTagLongerThanAnAddressHoldsIsntOne()
    {
        Assert.False(ModEvents.TryReadKey($"{Author.PublicKey}:{new string('x', 256)}", out _, out _));
    }

    [Theory]
    [InlineData("Fallout: New Vegas", "fallout: new vegas")]
    [InlineData("  Starfield ", "starfield")]
    public void AGamesKeyIsItsNameLowercase(string name, string key)
    {
        Assert.Equal(key, ModEvents.GameKey(name));
    }

    private static NostrEvent Recorded(string author, string d, int kind)
        => RecordedDegMods.RecordedEvents("brs.degmods.com").Concat(RecordedDegMods.RecordedEvents("relay.degmods.com"))
            .Where(nostrEvent => nostrEvent.PubKey == author && nostrEvent.D == d && nostrEvent.Kind == kind)
            .MaxBy(nostrEvent => nostrEvent.CreatedAt)!;

    private static NostrEvent Mod(string d, string title, long createdAt, params string[][] more)
        => Author.Sign(DegModsLink.ModKind, createdAt, [["d", d], ["title", title], ["g", "Game"], .. more]);

    private static IEnumerable<string[]> Old(string d, string[][] more)
        => [["d", d], ["t", "GameMod"], ["title", "An old mod"], ["game", "Game"], .. more];

    private static string Json(params (string Name, string Value)[] fields)
    {
        JsonObject json = [];
        foreach ((string name, string value) in fields)
        {
            json[name] = value;
        }

        return json.ToJsonString();
    }
}
