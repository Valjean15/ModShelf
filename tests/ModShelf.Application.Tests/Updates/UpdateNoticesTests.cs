using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Updates;

/// <summary>
/// The notice of new updates, for a library whose mods 1 to 4 are named SkyUI, SMAPI, Unofficial Patch and Address Library.
/// </summary>
public sealed class UpdateNoticesTests
{
    private static readonly IReadOnlyList<LibraryMod> Library =
    [
        Watched("1", "SkyUI"),
        Watched("2", "SMAPI"),
        Watched("3", "Unofficial Patch"),
        Watched("4", "Address Library"),
    ];

    [Fact]
    public void NamesTheModOfOneUpdate()
    {
        Assert.Equal(new Notification("1 new update", "SkyUI"), UpdateNotices.For([Update("1", "10")], Library));
    }

    [Fact]
    public void CountsTheUpdatesAndNamesEachModOnce()
    {
        Assert.Equal(new Notification("3 new updates", "SMAPI and SkyUI"), UpdateNotices.For([Update("2", "20"), Update("1", "10"), Update("2", "21")], Library));
    }

    [Fact]
    public void CountsEachUpdateOfAMod()
    {
        Assert.Equal(new Notification("2 new updates", "SkyUI"), UpdateNotices.For([Update("1", "10"), Update("1", "11")], Library));
    }

    [Fact]
    public void NamesTwoModsAndCountsTheOthers()
    {
        Notification notice = UpdateNotices.For([Update("1", "10"), Update("2", "20"), Update("3", "30"), Update("4", "40")], Library);

        Assert.Equal(new Notification("4 new updates", "SkyUI, SMAPI and 2 more"), notice);
    }

    [Fact]
    public void AModTheLibraryDoesntHaveGoesByItsId()
    {
        Assert.Equal("9", UpdateNotices.For([Update("9", "90")], Library).Message);
    }

    [Fact]
    public void NeedsAnUpdate()
    {
        Assert.Throws<ArgumentException>(() => UpdateNotices.For([], Library));
    }

    private static LibraryMod Watched(string mod, string name) => LibraryMod.Watch(FakeModSite.Mod(mod), name, null, null, TestData.Now);

    private static LibraryUpdate Update(string mod, string file)
        => new(new ModFileRef(FakeModSite.Mod(mod), file), $"File {file}", $"{file}.7z", null, ModFileCategory.Main, TestData.Now, null, TestData.Now);
}
