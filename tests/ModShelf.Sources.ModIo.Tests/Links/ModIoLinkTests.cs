using ModShelf.Sources.ModIo.Links;

namespace ModShelf.Sources.ModIo.Tests.Links;

/// <summary>
/// What a link to mod.io is to, by its shape alone.
/// </summary>
public sealed class ModIoLinkTests
{
    [Theory]
    [InlineData("https://mod.io/g/baldursgate3/m/better-inventory-ui1", "baldursgate3", "better-inventory-ui1")]
    [InlineData("https://WWW.MOD.IO/g/BaldursGate3/m/Better_Inventory-UI1/", "baldursgate3", "better_inventory-ui1")]
    [InlineData("https://mod.io/g/drg/m/7/files", "drg", "7")]
    public void AModsPageNamesItsGameAndItselfByTheirNamesInLinks(string link, string game, string mod)
    {
        Assert.Equal(new ModIoLink(game, mod, null, null, null), ModIoLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6795889/download", 6795889L)]
    [InlineData("https://u-1.modapi.io/v1/games/6715/mods/4228735/files/6795889", 6795889L)]
    [InlineData("https://API.mod.io/v1/games/6715/mods/4228735/", null)]
    public void AnApiLinkNamesItsGameModAndFileByTheirIds(string link, long? file)
    {
        Assert.Equal(new ModIoLink(null, null, 6715, 4228735, file), ModIoLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://image.modcdn.io/mods/7d37/4228735/logo.png")]
    [InlineData("https://g-6715.modapi.io.evil.example/v1/games/6715/mods/4228735")]
    [InlineData("https://x-6715.modapi.io/v1/games/6715/mods/4228735")]
    [InlineData("https://modapi.io/v1/games/6715/mods/4228735")]
    [InlineData("https://evilmod.io/g/baldursgate3/m/better-inventory-ui1")]
    [InlineData("mailto:support@mod.io")]
    public void ALinkElsewhereIsntModIos(string link)
    {
        Assert.Null(ModIoLink.Read(new Uri(link)));
    }

    [Fact]
    public void ARelativeLinkIsntModIos()
    {
        Assert.Null(ModIoLink.Read(new Uri("/g/baldursgate3/m/better-inventory-ui1", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://mod.io/g/baldursgate3/m/-starts-with-a-hyphen")]
    [InlineData("https://mod.io/g/baldurs.gate/m/better-inventory-ui1")]
    [InlineData("https://g-6715.modapi.io/v2/games/6715/mods/4228735")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6795889/download/again")]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods/1234567890123456789")]
    public void AnythingElseOfModIosIsRefused(string link)
    {
        Assert.Throws<ArgumentException>(() => ModIoLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("6715", true)]
    [InlineData("1", true)]
    [InlineData("999999999999999999", true)]
    [InlineData("0", false)]
    [InlineData("000", false)]
    [InlineData("-1", false)]
    [InlineData("+1", false)]
    [InlineData("1234567890123456789", false)]
    [InlineData("６７１５", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AnIdIsAPositiveNumberOfAtMost18Digits(string? text, bool isId)
    {
        Assert.Equal(isId, ModIoLink.IsId(text));
    }

    [Fact]
    public void AFilesLinkIsOnItsGamesApiHost()
    {
        Assert.Equal(
            new Uri("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6795889/download"),
            ModIoLink.FileLink(6715, 4228735, 6795889));
    }
}
