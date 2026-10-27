using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Library;

public sealed class LibraryUpdateTests
{
    private static readonly Uri Page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35408");

    [Fact]
    public void KeepsWhatTheSiteSaid()
    {
        LibraryUpdate update = Update(replaces: TestData.SkyUiMainFile);

        Assert.Equal((Ref("35408"), "SkyUI 5.3", "SkyUI_5_3_SE.7z", "5.3"), (update.File, update.Name, update.FileName, update.VersionLabel));
        Assert.Equal((ModFileCategory.Main, TestData.CreatedAt, TestData.SkyUiMainFile), (update.Category, update.UploadedAt, update.Replaces));
        Assert.Equal((TestData.CreatedAt.AddDays(1), Page), (update.FoundAt, update.PageUrl));
    }

    [Fact]
    public void ABlankVersionIsNone()
    {
        Assert.Null(Update(version: " ").VersionLabel);
    }

    [Fact]
    public void ReplacesOnlyAnotherFileOfItsMod()
    {
        Assert.Throws<ArgumentException>(() => Update(replaces: Ref("35408")));
        Assert.Throws<ArgumentException>(() => Update(replaces: new ModFileRef(new ModRef(TestData.Skyrim, "3863"), "1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    public void RefusesAFileNameThatIsntOne(string fileName)
    {
        Assert.Throws<ArgumentException>(() => Update(fileName: fileName));
    }

    [Fact]
    public void RefusesAnUnknownSection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryUpdate(Ref("35408"), "SkyUI 5.3", "SkyUI_5_3_SE.7z", "5.3", (ModFileCategory)99, TestData.CreatedAt, null, TestData.CreatedAt));
    }

    [Fact]
    public void RefusesAPageThatIsntAWebLink()
    {
        Assert.Throws<ArgumentException>(() => Update(page: new Uri("nxm://skyrimspecialedition/mods/12604/files/35408")));
    }

    [Fact]
    public void FoundSinceKeepsEverythingElse()
    {
        LibraryUpdate earlier = Update(replaces: TestData.SkyUiMainFile).FoundSince(TestData.CreatedAt);

        Assert.Equal((TestData.CreatedAt, Ref("35408"), TestData.SkyUiMainFile, Page), (earlier.FoundAt, earlier.File, earlier.Replaces, earlier.PageUrl));
    }

    private static ModFileRef Ref(string id) => new(TestData.SkyUi, id);

    private static LibraryUpdate Update(
        ModFileRef? replaces = null,
        string? version = "5.3",
        string fileName = "SkyUI_5_3_SE.7z",
        Uri? page = null)
        => new(Ref("35408"), "SkyUI 5.3", fileName, version, ModFileCategory.Main, TestData.CreatedAt, replaces, TestData.CreatedAt.AddDays(1), page ?? Page);
}
