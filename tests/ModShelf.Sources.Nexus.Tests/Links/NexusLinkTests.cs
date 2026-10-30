using ModShelf.Domain.Mods;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus.Tests.Links;

public sealed class NexusLinkTests
{
    [Fact]
    public void BuildsThePagesAndApiPathsItReads()
    {
        NexusLink mod = new("stardewvalley", "2400", FileId: null, IsHandoff: false);
        NexusLink file = mod with { FileId = "160380" };

        Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/2400", mod.PageUrl.AbsoluteUri);
        Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380", file.PageUrl.AbsoluteUri);
        Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380&nmm=1", file.HandoffPage.AbsoluteUri);
        Assert.Equal("https://www.nexusmods.com/stardewvalley", NexusLink.GamePage("stardewvalley").AbsoluteUri);
        Assert.Equal(("v1/games/stardewvalley/mods/2400", "v1/games/stardewvalley/mods/2400/files/160380"), (file.ModApiPath, file.FileApiPath));
        Assert.Equal("v1/games/stardewvalley", NexusLink.GameApiPath("stardewvalley"));
        Assert.Equal((mod, file, file), (NexusLink.Read(mod.PageUrl), NexusLink.Read(file.PageUrl), NexusLink.Read(file.HandoffPage)));
        Assert.Throws<InvalidOperationException>(() => mod.FileApiPath);
        Assert.Throws<InvalidOperationException>(() => mod.HandoffPage);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407")]
    [InlineData("https://nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407")]
    [InlineData("https://next.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407")]
    [InlineData("http://www.nexusmods.com/skyrimspecialedition/mods/12604/?file_id=35407&nmm=1")]
    [InlineData("https://WWW.NexusMods.com/SkyrimSpecialEdition/Mods/012604?tab=files&file_id=035407")]
    public void ReadsAFileOnTheWebsite(string link)
    {
        Assert.Equal(new NexusLink("skyrimspecialedition", "12604", "35407", IsHandoff: false), NexusLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604/")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=description")]
    public void ReadsAModOnTheWebsite(string link)
    {
        Assert.Equal(new NexusLink("skyrimspecialedition", "12604", null, IsHandoff: false), NexusLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407?key=a1b2c3&expires=1792810466&user_id=1000001")]
    [InlineData("nxm://SkyrimSpecialEdition/mods/12604/files/35407")]
    [InlineData("NXM://skyrimspecialedition/mods/12604/files/35407/")]
    public void ReadsAHandoff(string link)
    {
        Assert.Equal(new NexusLink("skyrimspecialedition", "12604", "35407", IsHandoff: true), NexusLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://example.com/skyrimspecialedition/mods/12604")]
    [InlineData("https://nexusmods.com.example.com/skyrimspecialedition/mods/12604")]
    [InlineData("ftp://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("file:///C:/nexusmods.com/skyrimspecialedition/mods/12604")]
    public void LeavesOtherLinksAlone(string link)
    {
        Assert.Null(NexusLink.Read(new Uri(link)));
    }

    [Fact]
    public void LeavesARelativeLinkAlone()
    {
        Assert.Null(NexusLink.Read(new Uri("skyrimspecialedition/mods/12604", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/news/12920")]
    [InlineData("https://next.nexusmods.com/skyrimspecialedition/collections/qdurkx")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604/extra")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/SkyUI")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/0")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/99999999999999999999")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?file_id=")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?file_id=-1")]
    [InlineData("https://www.nexusmods.com/skyrim.se/mods/12604")]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/12")]
    [InlineData("nxm://skyrimspecialedition/mods/12604")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407/more")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/abc")]
    public void RefusesItsOwnLinksToSomethingElse(string link)
    {
        Assert.Throws<ArgumentException>(() => NexusLink.Read(new Uri(link)));
    }

    [Fact]
    public void LinksToAFileOfTheSource()
    {
        ModFileRef file = new(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407");

        Assert.Equal(new NexusLink("skyrimspecialedition", "12604", "35407", IsHandoff: false), NexusLink.ForFile(file));
    }

    [Theory]
    [InlineData("mod-io", "skyrimspecialedition", "12604", "35407")]
    [InlineData("nexus", "skyrim/../se", "12604", "35407")]
    [InlineData("nexus", "skyrimspecialedition", "12604?x=1", "35407")]
    [InlineData("nexus", "skyrimspecialedition", "12604", "0")]
    public void RefusesAFileThatIsntOneOfTheSources(string source, string game, string mod, string fileId)
    {
        ModFileRef file = new(new ModRef(new GameRef(new SourceId(source), game), mod), fileId);

        Assert.Throws<ArgumentException>(() => NexusLink.ForFile(file));
    }

    [Theory]
    [InlineData("https://cf-files.nexusmods.com/cdn/1704/12604/SkyUI_5_2_SE.7z?expires=1792810466&md5=abc")]
    [InlineData("https://amsterdam-premium.nexus-cdn.com/1704/12604/SkyUI_5_2_SE.7z?expires=1792810466&md5=abc")]
    [InlineData("https://NEXUS-CDN.COM/1704/12604/SkyUI_5_2_SE.7z")]
    [InlineData("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/12604.json")]
    public void SaysWhenALinkIsToItsServersRatherThanAPage(string link)
    {
        // A download link from the file servers stops working soon, and saved as a direct link it would keep a
        // signed address; the API's own links aren't pages either.
        ArgumentException refused = Assert.Throws<ArgumentException>(() => NexusLink.Read(new Uri(link)));

        Assert.Contains("Copy the mod's page link", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/12")]
    [InlineData("https://next.nexusmods.com/skyrimspecialedition/collections/qdurkx")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx/mods")]
    public void SaysWhenALinkIsToACollection(string link)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => NexusLink.Read(new Uri(link)));

        Assert.Contains("not collections", refused.Message, StringComparison.Ordinal);
    }
}
