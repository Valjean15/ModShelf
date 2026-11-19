using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore.Tests.Links;

/// <summary>
/// The links to Thunderstore's packages and versions: on its website, in a community or not, and its <c>ror2mm://</c>
/// links.
/// </summary>
public sealed class ThunderstoreLinkTests
{
    [Theory]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/", "hades-ii", null)]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter", "hades-ii", null)]
    [InlineData("https://thunderstore.io/c/Hades-II/p/Jowday/DamageMeter/changelog/", "hades-ii", null)]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/v/1.2.3/", "hades-ii", "1.2.3")]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/v/1.2.3/required/", "hades-ii", "1.2.3")]
    [InlineData("https://www.thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/?tab=versions", "hades-ii", null)]
    [InlineData("https://thunderstore.io/package/download/Jowday/DamageMeter/1.2.3/", null, "1.2.3")]
    [InlineData("https://thunderstore.io/package/Jowday/DamageMeter/", null, null)]
    [InlineData("https://valheim.thunderstore.io/package/Jowday/DamageMeter/", null, null)]
    [InlineData("https://thunderstore.io/package/Jowday/DamageMeter/1.2.3/", null, "1.2.3")]
    public void ReadsTheWebsitesLinks(string link, string? community, string? version)
    {
        ThunderstoreLink parsed = ThunderstoreLink.Parse(new Uri(link))!;

        Assert.Equal((community, "Jowday-DamageMeter", version, false), (parsed.Community, parsed.Package.FullName, parsed.Version, parsed.IsHandoff));
    }

    [Fact]
    public void ReadsTheSchemesLinksAsHandoffsNamingNoCommunity()
    {
        ThunderstoreLink parsed = ThunderstoreLink.Parse(new Uri("ror2mm://v1/install/thunderstore.io/F-tank/ReapownedModPack/1.0.4/"))!;

        Assert.Equal((null, "F-tank", "ReapownedModPack", "1.0.4", true), (parsed.Community, parsed.Package.Team, parsed.Package.Name, parsed.Version, parsed.IsHandoff));
    }

    [Theory]
    [InlineData("https://example.com/c/hades-ii/p/Jowday/DamageMeter/")]
    [InlineData("https://thunderstore.io.example.com/c/hades-ii/p/Jowday/DamageMeter/")]
    [InlineData("https://ccdn.thunderstore.io/live/repository/icons/Jowday-DamageMeter-1.2.3.png")]
    [InlineData("https://gcdn.thunderstore.io/live/repository/packages/Jowday-DamageMeter-1.2.3.zip")]
    [InlineData("ftp://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/")]
    [InlineData("file:///c/hades-ii/p/Jowday/DamageMeter/")]
    [InlineData("nxm://skyrimspecialedition/mods/1/files/2")]
    public void LeavesOtherLinksAlone(string link)
    {
        Assert.Null(ThunderstoreLink.Parse(new Uri(link)));
    }

    [Theory]
    [InlineData("https://thunderstore.io/c/hades-ii/")]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/")]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/Damage-Meter/")]
    [InlineData("https://thunderstore.io/c/hades-ii/p/Jowday/DamageMeter/v/1.2/")]
    [InlineData("https://thunderstore.io/c/hades_ii/p/Jowday/DamageMeter/")]
    [InlineData("https://thunderstore.io/package/download/Jowday/DamageMeter/latest/")]
    [InlineData("ror2mm://v1/install/thunderstore.example/Jowday/DamageMeter/1.2.3/")]
    [InlineData("ror2mm://v1/install/thunderstore.io/Jowday/DamageMeter/")]
    public void RefusesItsOwnLinksThatArentToAPackage(string link)
    {
        Assert.Throws<ArgumentException>(() => ThunderstoreLink.Parse(new Uri(link)));
    }

    [Fact]
    public void GivesPagesInACommunity()
    {
        Thunderstore.Catalog.PackageName package = Thunderstore.Catalog.PackageName.Parse("F-tank-ReapownedModPack")!;

        Assert.Equal(
            ("https://thunderstore.io/c/repo/p/F-tank/ReapownedModPack/", "https://thunderstore.io/c/repo/p/F-tank/ReapownedModPack/v/1.0.4/"),
            (ThunderstoreLink.Page("repo", package).AbsoluteUri, ThunderstoreLink.Page("repo", package, "1.0.4").AbsoluteUri));
    }
}
