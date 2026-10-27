using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Library;

public sealed class LibraryModTests
{
    private static readonly Uri Page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");
    private static readonly DateTimeOffset Later = TestData.CreatedAt.AddDays(3);

    [Fact]
    public void AModStartsWatchedWithoutFiles()
    {
        LibraryMod mod = LibraryMod.Watch(TestData.SkyUi, "SkyUI", "schlangster", Page, TestData.CreatedAt);

        Assert.Equal((TestData.SkyUi, "SkyUI", "schlangster", Page), (mod.Mod, mod.Name, mod.Author, mod.PageUrl));
        Assert.True(mod.IsWatched);
        Assert.Equal(TestData.CreatedAt, mod.AddedAt);
        Assert.Null(mod.CheckedAt);
        Assert.Empty(mod.Files);
    }

    [Fact]
    public void ABlankAuthorIsNone()
    {
        Assert.Null(LibraryMod.Watch(TestData.SkyUi, "SkyUI", " ", null, TestData.CreatedAt).Author);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    public void RefusesAPageThatIsntAWebLink(string page)
    {
        Assert.Throws<ArgumentException>(() => LibraryMod.Watch(TestData.SkyUi, "SkyUI", null, new Uri(page), TestData.CreatedAt));
        Assert.Throws<ArgumentException>(() => Watched().WithDetails("SkyUI", null, new Uri(page)));
    }

    [Fact]
    public void RefusesABlankName()
    {
        Assert.Throws<ArgumentException>(() => LibraryMod.Watch(TestData.SkyUi, " ", null, null, TestData.CreatedAt));
        Assert.Throws<ArgumentException>(() => Watched().WithDetails("", null, null));
    }

    [Fact]
    public void ChangesLeaveTheOriginalAsItWas()
    {
        LibraryMod original = Watched();

        original.WithFile(File("35407")).Checked(Later).WithDetails("SkyUI SE", null, null).Unwatched();

        Assert.Equal(("SkyUI", "schlangster", true), (original.Name, original.Author, original.IsWatched));
        Assert.Null(original.CheckedAt);
        Assert.Empty(original.Files);
    }

    [Fact]
    public void AModWithoutFilesLeavesTheLibraryWhenUnwatched()
    {
        Assert.Null(Watched().Unwatched());
    }

    [Fact]
    public void AModWithFilesStaysUnwatched()
    {
        LibraryMod? unwatched = Watched().WithFile(File("35407")).Unwatched();

        Assert.NotNull(unwatched);
        Assert.False(unwatched.IsWatched);
        Assert.Single(unwatched.Files);
        Assert.True(unwatched.Watched().IsWatched);
    }

    [Fact]
    public void WatchingAWatchedModChangesNothing()
    {
        LibraryMod mod = Watched();

        Assert.Same(mod, mod.Watched());
    }

    [Fact]
    public void KeepsItsDetailsUpToDate()
    {
        LibraryMod mod = Watched().WithFile(File("35407")).Checked(Later);
        Uri page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=description");

        LibraryMod renamed = mod.WithDetails("SkyUI SE", null, page);

        Assert.Equal(("SkyUI SE", (string?)null, page), (renamed.Name, renamed.Author, renamed.PageUrl));
        Assert.Equal((mod.IsWatched, mod.AddedAt, mod.CheckedAt), (renamed.IsWatched, renamed.AddedAt, renamed.CheckedAt));
        Assert.Equal(mod.Files, renamed.Files);
    }

    [Fact]
    public void RemembersWhenItWasChecked()
    {
        Assert.Equal(Later, Watched().Checked(Later).CheckedAt);
    }

    [Fact]
    public void AddsFilesInOrderAndReplacesTheSameFile()
    {
        LibraryFile first = File("1", "SkyUI_1.7z");
        LibraryFile second = File("2", "SkyUI_2.7z");
        LibraryFile firstAgain = File("1", "SkyUI_1b.7z");

        LibraryMod mod = Watched().WithFile(first).WithFile(second).WithFile(firstAgain);

        Assert.Equal([firstAgain, second], mod.Files);
    }

    [Fact]
    public void RefusesAnotherModsFile()
    {
        LibraryFile other = new(
            new ModFileRef(new ModRef(TestData.Skyrim, "3863"), "1"), "SKSE", null, ModFileCategory.Main, TestData.CreatedAt, "SKSE.7z", 1, TestData.CreatedAt);

        Assert.Throws<ArgumentException>(() => Watched().WithFile(other));
    }

    [Theory]
    [InlineData("SkyUI.7z")]
    [InlineData("SKYUI.7Z")]
    public void RefusesTwoFilesAtTheSamePath(string path)
    {
        LibraryMod mod = Watched().WithFile(File("1", "SkyUI.7z"));

        Assert.Throws<ArgumentException>(() => mod.WithFile(File("2", path)));
    }

    [Fact]
    public void AFileCanMoveToAnotherPath()
    {
        LibraryMod mod = Watched().WithFile(File("1", "SkyUI.7z"));

        Assert.Equal("Old/SkyUI.7z", Assert.Single(mod.WithFile(File("1", "Old/SkyUI.7z")).Files).RelativePath);
    }

    [Fact]
    public void RemovingAFileKeepsAWatchedMod()
    {
        LibraryMod? mod = Watched().WithFile(File("1")).WithoutFile(Ref("1"));

        Assert.NotNull(mod);
        Assert.Empty(mod.Files);
    }

    [Fact]
    public void RemovingTheLastFileOfAnUnwatchedModTakesItOutOfTheLibrary()
    {
        LibraryMod unwatched = Watched().WithFile(File("1")).WithFile(File("2", "Other.7z")).Unwatched()!;

        LibraryMod? withOne = unwatched.WithoutFile(Ref("1"));

        Assert.Equal("2", Assert.Single(withOne!.Files).File.Id);
        Assert.Null(withOne.WithoutFile(Ref("2")));
    }

    [Fact]
    public void RemovingAFileItDoesntHaveChangesNothingElse()
    {
        LibraryMod mod = Watched().WithFile(File("1"));

        Assert.Single(mod.WithoutFile(Ref("2"))!.Files);
    }

    [Fact]
    public void RestoresWhatWasStored()
    {
        LibraryFile file = File("1");

        LibraryMod mod = LibraryMod.Restore(TestData.SkyUi, "SkyUI", "schlangster", Page, isWatched: false, TestData.CreatedAt, Later, [file]);

        Assert.Equal(
            (TestData.SkyUi, "SkyUI", "schlangster", Page, false, TestData.CreatedAt, (DateTimeOffset?)Later),
            (mod.Mod, mod.Name, mod.Author, mod.PageUrl, mod.IsWatched, mod.AddedAt, mod.CheckedAt));
        Assert.Same(file, Assert.Single(mod.Files));
    }

    [Fact]
    public void RestoringRefusesWhatNoChangeCanProduce()
    {
        LibraryFile other = new(
            new ModFileRef(new ModRef(TestData.Skyrim, "3863"), "1"), "SKSE", null, ModFileCategory.Main, TestData.CreatedAt, "SKSE.7z", 1, TestData.CreatedAt);

        Assert.Throws<ArgumentException>(() => Restore(isWatched: false));
        Assert.Throws<ArgumentException>(() => Restore(isWatched: true, other));
        Assert.Throws<ArgumentException>(() => Restore(isWatched: true, File("1", "a.7z"), File("1", "b.7z")));
        Assert.Throws<ArgumentException>(() => Restore(isWatched: true, File("1", "a.7z"), File("2", "A.7z")));
        Assert.Throws<ArgumentException>(() => Restore(isWatched: true, File("1"), null!));
    }

    private static LibraryMod Watched() => LibraryMod.Watch(TestData.SkyUi, "SkyUI", "schlangster", Page, TestData.CreatedAt);

    private static ModFileRef Ref(string id) => new(TestData.SkyUi, id);

    private static LibraryFile File(string id, string path = "SkyUI_5_2_SE.7z")
        => new(Ref(id), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.CreatedAt, path, 2_500_000, Later);

    private static LibraryMod Restore(bool isWatched, params LibraryFile[] files)
        => LibraryMod.Restore(TestData.SkyUi, "SkyUI", null, null, isWatched, TestData.CreatedAt, null, files);
}
