using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.Thunderstore.Tests.Common;

using static ModShelf.Sources.Thunderstore.Tests.Common.ThunderstoreHarness;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// What a package needs, which packages changed, and where a version downloads from.
/// </summary>
public sealed class ThunderstoreSourceRequirementTests : IDisposable
{
    private static readonly DateTimeOffset January = TestPackage.January;

    private readonly ThunderstoreHarness _harness = new();

    public ThunderstoreSourceRequirementTests()
    {
        _harness.List(
            new TestPackage("Jowday", "DamageMeter")
            {
                Updated = January.AddDays(3),
                Versions = [new("1.2.0", January.AddDays(2)), new("1.1.0", January)],
                Dependencies = ["Hell2Modding-Hell2Modding-1.0.78", "bbepis-BepInExPack-5.4.2113", "not a dependency"],
            },
            new TestPackage("Hell2Modding", "Hell2Modding") { Updated = January.AddDays(-2) },
            new TestPackage("Old", "Deprecated") { Updated = January.AddDays(1), Deprecated = true });
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheNewestVersionsDependenciesPointingToThoseOfAnotherGame()
    {
        IReadOnlyList<ModRequirement> requirements = await _harness.Source.GetRequirementsAsync(Mod("Jowday-DamageMeter"), Cancellation);

        Assert.Equal(2, requirements.Count);
        Assert.Equal(
            ("Hell2Modding by Hell2Modding", Mod("Hell2Modding-Hell2Modding"), "https://thunderstore.io/c/game/p/Hell2Modding/Hell2Modding/", "Version 1.0.78 or later"),
            (requirements[0].Name, requirements[0].Mod, requirements[0].PageUrl?.AbsoluteUri, requirements[0].Notes));
        Assert.Equal(
            ("BepInExPack by bbepis", null, "https://thunderstore.io/package/bbepis/BepInExPack/", "Version 5.4.2113 or later. Listed on Thunderstore, but not for this game"),
            (requirements[1].Name, requirements[1].Mod, requirements[1].PageUrl?.AbsoluteUri, requirements[1].Notes));
        Assert.Contains(_harness.Server.Logs.Entries, entry => entry.Contains("names a dependency that isn't Team-Name-1.2.3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsThePackagesUpdatedSinceDeprecatedOnesToo()
    {
        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(Game, January, Cancellation);

        Assert.Equal(
            [
                ("Jowday-DamageMeter", January.AddDays(3), (DateTimeOffset?)January.AddDays(2)),
                ("Old-Deprecated", January.AddDays(1), January),
            ],
            updates.Select(update => (update.Mod.Id, update.LastActivityAt, update.FilesUpdatedAt)).OrderBy(update => update.Id, StringComparer.Ordinal));
        Assert.Equal("https://thunderstore.io/c/game/p/Jowday/DamageMeter/", updates.Single(update => update.Mod.Id == "Jowday-DamageMeter").PageUrl?.AbsoluteUri);
    }

    [Fact]
    public async Task ReachesAYearBack()
    {
        _harness.Server.Clock.SetUtcNow(January.AddDays(400));

        Assert.Equal(TimeSpan.FromDays(365), _harness.Source.Reach);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _harness.Source.GetUpdatesAsync(Game, January, Cancellation));
        Assert.Empty(await _harness.Source.GetUpdatesAsync(Game, January.AddDays(36), Cancellation));
    }

    [Fact]
    public async Task DownloadsAVersionFromItsDownloadLinkWithoutAsking()
    {
        ResolveResult resolved = await _harness.Source.ResolveAsync(File("F-tank-Pack", "1.0.4"), handoff: null, Cancellation);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(resolved);
        Assert.Equal(new Uri(_harness.Server.BaseAddress, "package/download/F-tank/Pack/1.0.4/"), ticket.Url);
        Assert.Null(ticket.ExpectedHash);
        Assert.Empty(_harness.Server.Asked);
    }

    [Theory]
    [InlineData("Team-Mod", "latest")]
    [InlineData("TeamMod", "1.0.0")]
    public async Task RefusesAFileThatIsntAVersionOfAPackage(string mod, string version)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.ResolveAsync(File(mod, version), handoff: null, Cancellation));
    }
}
