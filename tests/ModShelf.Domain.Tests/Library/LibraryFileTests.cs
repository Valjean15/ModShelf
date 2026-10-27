using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Library;

public sealed class LibraryFileTests
{
    [Fact]
    public void KeepsWhatItIsGiven()
    {
        DateTimeOffset uploaded = new(2026, 3, 14, 19, 7, 23, TimeSpan.Zero);

        LibraryFile file = new(
            TestData.SkyUiMainFile, "SkyUI 5.2", "5.2", ModFileCategory.Main, uploaded, "nexus/skyrimspecialedition/12604/SkyUI_5_2_SE.7z", 2_500_000, TestData.CreatedAt);

        Assert.Equal(
            (TestData.SkyUiMainFile, "SkyUI 5.2", "5.2", ModFileCategory.Main, uploaded, "nexus/skyrimspecialedition/12604/SkyUI_5_2_SE.7z", 2_500_000L, TestData.CreatedAt),
            (file.File, file.Name, file.VersionLabel, file.Category, file.UploadedAt, file.RelativePath, file.SizeInBytes, file.DownloadedAt));
    }

    [Theory]
    [InlineData("SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z")]
    [InlineData("nexus/skyrimspecialedition/12604/SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z")]
    public void ItsFileNameIsTheLastPartOfItsPath(string path, string fileName)
    {
        Assert.Equal(fileName, File(path).FileName);
    }

    [Fact]
    public void ABlankVersionLabelIsNone()
    {
        LibraryFile file = new(TestData.SkyUiMainFile, "SkyUI", " ", ModFileCategory.Main, TestData.CreatedAt, "SkyUI.7z", 1, TestData.CreatedAt);

        Assert.Null(file.VersionLabel);
    }

    [Theory]
    [InlineData("/nexus/SkyUI.7z")]
    [InlineData("nexus/")]
    [InlineData("nexus//SkyUI.7z")]
    [InlineData("nexus/../SkyUI.7z")]
    [InlineData("./SkyUI.7z")]
    [InlineData("nexus\\SkyUI.7z")]
    [InlineData("C:/SkyUI.7z")]
    [InlineData(" ")]
    public void RefusesAPathThatLeavesTheLibrary(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => File(path));
    }

    [Fact]
    public void RefusesABlankName()
    {
        Assert.Throws<ArgumentException>(
            () => new LibraryFile(TestData.SkyUiMainFile, " ", null, ModFileCategory.Main, TestData.CreatedAt, "SkyUI.7z", 1, TestData.CreatedAt));
    }

    [Fact]
    public void RefusesAnUnknownCategoryOrANegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryFile(TestData.SkyUiMainFile, "SkyUI", null, (ModFileCategory)99, TestData.CreatedAt, "SkyUI.7z", 1, TestData.CreatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryFile(TestData.SkyUiMainFile, "SkyUI", null, ModFileCategory.Main, TestData.CreatedAt, "SkyUI.7z", -1, TestData.CreatedAt));
    }

    private static LibraryFile File(string path)
        => new(TestData.SkyUiMainFile, "SkyUI", null, ModFileCategory.Main, TestData.CreatedAt, path, 1, TestData.CreatedAt);
}
