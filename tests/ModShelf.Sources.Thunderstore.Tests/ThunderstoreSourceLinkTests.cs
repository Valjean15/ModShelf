using System.Net;

using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Thunderstore.Tests.Common;

using static ModShelf.Sources.Thunderstore.Tests.Common.ThunderstoreHarness;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// Links recognized as packages' versions: one in a community from its listing, one that names none after asking
/// Thunderstore which community lists its package.
/// </summary>
public sealed class ThunderstoreSourceLinkTests : IDisposable
{
    private static readonly DateTimeOffset January = TestPackage.January;

    private readonly ThunderstoreHarness _harness = new();

    public ThunderstoreSourceLinkTests()
    {
        _harness.List(new TestPackage("Jowday", "DamageMeter") { Versions = [new("1.2.0", January.AddDays(1)), new("1.1.0", January)] });
    }

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("https://thunderstore.io/c/game/p/Jowday/DamageMeter/", "1.2.0")]
    [InlineData("https://thunderstore.io/c/game/p/jowday/damagemeter/versions/", "1.2.0")]
    [InlineData("https://thunderstore.io/c/game/p/Jowday/DamageMeter/v/1.1.0/", "1.1.0")]
    public async Task TakesALinkInTheCommunityAsTheVersionItsTo(string link, string version)
    {
        RecognizedLink recognized = (await _harness.Source.RecognizeAsync(new Uri(link), Cancellation))!;

        Assert.Equal(File("Jowday-DamageMeter", version), recognized.File);
        Assert.Equal(($"Jowday-DamageMeter-{version}.zip", false), (recognized.SuggestedFileName, recognized.IsHandoff));
    }

    [Fact]
    public async Task TakesTheSchemesLinkAsAHandoffInTheOneCommunityThatListsIt()
    {
        _harness.Server.ServeDetail("Jowday", "DamageMeter", "1.2.0", Community);

        RecognizedLink recognized = (await _harness.Source.RecognizeAsync(new Uri("ror2mm://v1/install/thunderstore.io/Jowday/DamageMeter/1.1.0/"), Cancellation))!;

        Assert.Equal((File("Jowday-DamageMeter", "1.1.0"), true), (recognized.File, recognized.IsHandoff));
    }

    [Fact]
    public async Task TakesTheDownloadLinkAndTheOldPage()
    {
        _harness.Server.ServeDetail("Jowday", "DamageMeter", "1.2.0", Community);

        RecognizedLink download = (await _harness.Source.RecognizeAsync(new Uri("https://thunderstore.io/package/download/Jowday/DamageMeter/1.1.0/"), Cancellation))!;
        RecognizedLink page = (await _harness.Source.RecognizeAsync(new Uri("https://thunderstore.io/package/Jowday/DamageMeter/"), Cancellation))!;

        Assert.Equal((File("Jowday-DamageMeter", "1.1.0"), File("Jowday-DamageMeter", "1.2.0")), (download.File, page.File));
    }

    [Fact]
    public async Task RefusesALinkNamingNoCommunityToAPackageListedInSeveral()
    {
        _harness.Server.ServeDetail("BepInEx", "BepInExPack", "5.4.2305", "ultrakill", "repo", "valheim", "game");

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.RecognizeAsync(new Uri("ror2mm://v1/install/thunderstore.io/BepInEx/BepInExPack/5.4.2305/"), Cancellation));

        Assert.Equal(
            "BepInEx-BepInExPack is listed for 4 games on Thunderstore, such as ultrakill, repo, valheim, and the link doesn't say "
            + "which. Use the link of its page in the game's community, such as https://thunderstore.io/c/ultrakill/p/BepInEx/BepInExPack/.",
            refused.Message);
    }

    [Fact]
    public async Task SaysWhenNoCommunityListsThePackage()
    {
        _harness.Server.ServeDetail("Team", "Unlisted", "1.0.0");

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.RecognizeAsync(new Uri("https://thunderstore.io/package/download/Team/Unlisted/1.0.0/"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
    }

    [Theory]
    [InlineData("https://thunderstore.io/c/game/p/Jowday/Other/")]
    [InlineData("https://thunderstore.io/c/game/p/Jowday/DamageMeter/v/9.9.9/")]
    public async Task SaysWhenTheCommunityDoesntListThePackageOrTheVersion(string link)
    {
        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.Source.RecognizeAsync(new Uri(link), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
    }

    [Fact]
    public async Task LeavesOtherSitesLinksAloneWithoutAsking()
    {
        Assert.Null(await _harness.Source.RecognizeAsync(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), Cancellation));
        Assert.Empty(_harness.Server.Asked);
    }

    [Fact]
    public void HandsOverLinksOfItsOwnScheme()
    {
        Assert.Equal(("ror2mm", true), (_harness.Source.Scheme, _harness.Source.HandsOverLinks));
    }
}
