using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus.Tests.Links;

/// <summary>
/// Links to Nexus Mods' collections: their pages, old and new, and the nxm:// links their Download button hands over.
/// </summary>
public sealed class NexusCollectionLinkTests
{
    [Theory]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", null)]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/mods", null)]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/revisions/12", 12)]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/revisions/12/mods", 12)]
    [InlineData("https://next.nexusmods.com/skyrimspecialedition/collections/qdurkx", null)]
    [InlineData("https://next.nexusmods.com/SkyrimSpecialEdition/Collections/QDURKX/Revisions/3", 3)]
    [InlineData("http://nexusmods.com/games/skyrimspecialedition/collections/qdurkx?tab=mods", null)]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/12", 12)]
    [InlineData("nxm://SkyrimSpecialEdition/collections/qdurkx", null)]
    public void ReadsACollectionsGameSlugAndRevision(string link, int? revision)
    {
        NexusCollectionLink? read = NexusCollectionLink.Read(new Uri(link));

        Assert.NotNull(read);
        Assert.Equal(new NexusCollectionLink("skyrimspecialedition", "qdurkx", revision), read);
        Assert.Equal("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", read.PageUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections")]
    [InlineData("https://www.nexusmods.com/collections")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    [InlineData("https://example.com/games/skyrimspecialedition/collections/qdurkx")]
    [InlineData("ftp://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx")]
    [InlineData("file:///games/skyrimspecialedition/collections/qdurkx")]
    public void IgnoresLinksThatArentToACollection(string link)
    {
        Assert.Null(NexusCollectionLink.Read(new Uri(link)));
    }

    [Fact]
    public void IgnoresARelativeLink()
    {
        Assert.Null(NexusCollectionLink.Read(new Uri("games/skyrimspecialedition/collections/qdurkx", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/games/skyrim%20se/collections/qdurkx", "no game")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdur-kx", "doesn't name a collection")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/revisions", "isn't a number")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/revisions/latest", "isn't a number")]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/0", "isn't a number")]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/99999999999", "isn't a number")]
    public void RefusesACollectionLinkItCantRead(string link, string said)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => NexusCollectionLink.Read(new Uri(link)));

        Assert.Contains(said, refused.Message, StringComparison.Ordinal);
    }
}
