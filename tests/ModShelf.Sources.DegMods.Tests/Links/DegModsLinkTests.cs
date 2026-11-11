using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods.Tests.Links;

public sealed class DegModsLinkTests
{
    private const string Author = "16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1";
    private const string D = "7cce3021-6514-47f7-a054-a28dc1269ff4";

    private static readonly Uri Site = DegModsOptions.DefaultSiteAddress;
    private static readonly string Naddr = Nip19.Naddr(DegModsLink.ModKind, Author, D);

    [Theory]
    [InlineData("https://degmods.com/mod/{0}")]
    [InlineData("https://degmods.com/mod/{0}/")]
    [InlineData("https://www.degmods.com/mod/{0}")]
    [InlineData("http://DEGMODS.com/mod/{0}?ref=x")]
    [InlineData("nostr:{0}")]
    public void ReadsAModsPageOrAddress(string format)
    {
        DegModsLink? link = DegModsLink.Read(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Naddr)), Site);

        Assert.Equal(new DegModsLink(DegModsLink.ModKind, Author, D, FileId: null), link);
    }

    [Fact]
    public void ReadsAnOlderListingsAddress()
    {
        DegModsLink? link = DegModsLink.Read(new Uri($"https://degmods.com/mod/{Nip19.Naddr(DegModsLink.OldModKind, Author, D)}"), Site);

        Assert.Equal(new DegModsLink(DegModsLink.OldModKind, Author, D, FileId: null), link);
    }

    [Fact]
    public void ReadsTheFileModShelfsLinkNames()
    {
        Uri page = DegModsLink.FilePage(Site, DegModsLink.ModKind, Author, D, "550a7a5da7893d23");

        Assert.Equal($"https://degmods.com/mod/{Naddr}#file=550a7a5da7893d23", page.AbsoluteUri);
        Assert.Equal(new DegModsLink(DegModsLink.ModKind, Author, D, "550a7a5da7893d23"), DegModsLink.Read(page, Site));
    }

    [Fact]
    public void AnyOtherFragmentIsTheMod()
    {
        Assert.Null(DegModsLink.Read(new Uri($"https://degmods.com/mod/{Naddr}#comments"), Site)!.FileId);
    }

    [Theory]
    [InlineData("https://example.com/mod/{0}")]
    [InlineData("https://degmods.com.example.com/mod/{0}")]
    [InlineData("ftp://degmods.com/mod/{0}")]
    [InlineData("nostr:npub17jl3ldd6305rnacvwvchx03snauqsg4nz8mruq0emj9thdpglr2sst825x")]
    [InlineData("nostr:nonsense")]
    public void IsntDegModsLinkWhenItIsAnotherSitesOrAnotherNostrThing(string format)
    {
        Assert.Null(DegModsLink.Read(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Naddr)), Site));
    }

    [Fact]
    public void ANostrAddressOfAnotherKindIsntDegModsLink()
    {
        Assert.Null(DegModsLink.Read(new Uri($"nostr:{Nip19.Naddr(30023, Author, D)}"), Site));
    }

    [Theory]
    [InlineData("https://degmods.com/")]
    [InlineData("https://degmods.com/game/Starfield")]
    [InlineData("https://degmods.com/mod/not-an-address")]
    [InlineData("https://degmods.com/mod/{0}/edit")]
    [InlineData("https://degmods.com/mods")]
    [InlineData("https://degmods.com/game/{0}")]
    public void RefusesDegModsLinksToSomethingOtherThanAMod(string format)
    {
        Assert.Throws<ArgumentException>(() => DegModsLink.Read(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Naddr)), Site));
    }

    [Fact]
    public void RefusesAModJam()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => DegModsLink.Read(new Uri($"https://degmods.com/mod/{Nip19.Naddr(31143, Author, D)}"), Site));

        Assert.Contains("mod jam", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file=")]
    [InlineData("file=550A7A5DA7893D23")]
    [InlineData("file=550a7a5da7893d2")]
    [InlineData("file=550a7a5da7893d23a")]
    [InlineData("file=../../etc")]
    public void RefusesAFileModShelfDoesntName(string fragment)
    {
        Assert.Throws<ArgumentException>(() => DegModsLink.Read(new Uri($"https://degmods.com/mod/{Naddr}#{fragment}"), Site));
    }

    [Fact]
    public void AGamesPageEscapesItsName()
    {
        Assert.Equal("https://degmods.com/game/Fallout%3A%20New%20Vegas", DegModsLink.GamePage(Site, "Fallout: New Vegas").AbsoluteUri);
    }

    [Theory]
    [InlineData("550a7a5da7893d23", true)]
    [InlineData("550A7A5DA7893D23", false)]
    [InlineData("550a7a5da7893d2g", false)]
    [InlineData(null, false)]
    public void AFilesIdIsSixteenLowercaseHexadecimalDigits(string? id, bool valid)
    {
        Assert.Equal(valid, DegModsLink.IsFileId(id));
    }
}
