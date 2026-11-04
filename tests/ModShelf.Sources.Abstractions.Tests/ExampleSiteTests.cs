using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Sources.Abstractions.Tests;

/// <summary>
/// The example site keeps the contract every source keeps, though it's shaped unlike Nexus Mods: no handoff, no
/// account, no update feed, game keys with hyphens and mod IDs of letters, and a search with some orders and filters
/// and smaller pages.
/// </summary>
public sealed class ExampleSiteTests : SourceContractTests<ExampleSite>
{
    protected override SourceId ExpectedId { get; } = new("example");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        ExampleSite.FileLink(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), ExampleSite.BetterCrops10),
        ExampleSite.ModLink(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.Hoes)),
        new("https://MODS.example.org/pixel-farm/mod/ptab"),
    ];

    /// <summary>
    /// Pixel Farm, with 55 mods: more than a page of 50.
    /// </summary>
    protected override GameRef SearchableGame => ExampleSite.PixelFarm.Game;

    protected override ExampleSite CreateSource() => new();
}
