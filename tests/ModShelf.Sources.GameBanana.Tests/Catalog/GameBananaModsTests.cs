using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests.Catalog;

/// <summary>
/// A mod as GameBanana describes it, in ModShelf's words: whether it can be had, and the links the app may open.
/// </summary>
public sealed class GameBananaModsTests
{
    private static readonly ModRef Mod = GameBananaHarness.Mod;

    [Theory]
    [InlineData("https://gamebanana.com/mods/440088", true)]
    [InlineData("http://gamebanana.com/mods/440088", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://gamebanana.com/mods/440088", false)]
    [InlineData("/mods/440088", false)]
    [InlineData(null, false)]
    public void OnlyAWebLinkIsALink(string? text, bool isOne)
    {
        Assert.Equal(isOne, GameBananaMods.WebLink(text) is not null);
    }

    [Fact]
    public void AModWhosePageIsntAWebLinkHasItsPageOnGameBanana()
    {
        ModInfo mod = GameBananaMods.ToModInfo(Mod.Game, Profile() with { ProfileUrl = "javascript:alert(1)" }, category: null);

        Assert.Equal(new Uri("https://gamebanana.com/mods/440088"), mod.PageUrl);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void AModTrashedWithheldOrPrivateCantBeHad(bool trashed, bool withheld, bool hidden, bool available)
    {
        ModInfo mod = GameBananaMods.ToModInfo(Mod.Game, Profile() with { IsTrashed = trashed, IsWithheld = withheld, IsPrivate = hidden }, category: null);

        Assert.Equal(available, mod.IsAvailable);
    }

    private static GameBananaMod Profile() => new() { Id = 440088, Name = "P5R Female Protagonist" };
}
