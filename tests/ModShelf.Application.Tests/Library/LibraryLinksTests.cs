using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Library;

public sealed class LibraryLinksTests
{
    private static readonly ModRef SkyUi = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "12604");
    private static readonly ModRef Skse = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "30379");

    private static readonly LibraryMod[] Library =
    [
        LibraryMod.Watch(Skse, "SKSE", null, null, TestData.Now),
        LibraryMod.Watch(SkyUi, "SkyUI", null, new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604"), TestData.Now),
    ];

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("http://NexusMods.com/SkyrimSpecialEdition/mods/12604/")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407")]
    public void FindsAModByItsPageOrALinkOnIt(string link)
    {
        Assert.Equal(SkyUi, LibraryLinks.ModAt(Library, new Uri(link)));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/1260")]
    [InlineData("https://www.nexusmods.com/skyrim/mods/12604")]
    [InlineData("https://nexusmods.example/skyrimspecialedition/mods/12604")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    public void FindsNoneForAnotherPage(string link)
    {
        Assert.Null(LibraryLinks.ModAt(Library, new Uri(link)));
    }
}
