using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Mods;

public sealed class ModFileTests
{
    private static readonly FileHash Md5 = new(FileHashAlgorithm.Md5, TestData.Md5);

    [Fact]
    public void KeepsWhatTheSourceDescribed()
    {
        ModFile file = Create();

        Assert.Equal(TestData.SkyUiMainFile, file.Reference);
        Assert.Equal("Main File", file.Name);
        Assert.Equal("SkyUI_5_2_SE.7z", file.FileName);
        Assert.Equal("5.2SE", file.VersionLabel);
        Assert.Equal(ModFileCategory.Main, file.Category);
        Assert.Equal(2_500_000, file.SizeInBytes);
        Assert.Equal(TestData.CreatedAt, file.UploadedAt);
        Assert.Equal(Md5, Assert.Single(file.Hashes));
        Assert.Null(file.PageUrl);
        Assert.Null(file.ReplacedBy);
    }

    [Fact]
    public void KeepsTheFileThatReplacedIt()
    {
        ModFileRef newer = new(TestData.SkyUi, "35408");

        Assert.Equal(newer, Create(replacedBy: newer).ReplacedBy);
    }

    [Fact]
    public void IsReplacedOnlyByAnotherFileOfItsMod()
    {
        Assert.Throws<ArgumentException>(() => Create(replacedBy: TestData.SkyUiMainFile));
        Assert.Throws<ArgumentException>(() => Create(replacedBy: new ModFileRef(new ModRef(TestData.Skyrim, "3863"), "35408")));
    }

    [Fact]
    public void KeepsItsPage()
    {
        Uri page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");

        Assert.Same(page, Create(pageUrl: page).PageUrl);
    }

    [Theory]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void RejectsAPageThatIsntAWebLink(string page)
    {
        Assert.Throws<ArgumentException>(() => Create(pageUrl: new Uri(page)));
    }

    [Fact]
    public void RejectsARelativePage()
    {
        Assert.Throws<ArgumentException>(() => Create(pageUrl: new Uri("mods/12604", UriKind.Relative)));
    }

    [Fact]
    public void KeepsItsOwnCopyOfTheHashes()
    {
        List<FileHash> hashes = [Md5];
        ModFile file = Create(hashes: hashes);

        hashes.Clear();

        Assert.Single(file.Hashes);
    }

    [Fact]
    public void AllowsAFileWithoutHashesOrSize()
    {
        ModFile file = Create(sizeInBytes: null, hashes: []);

        Assert.Null(file.SizeInBytes);
        Assert.Empty(file.Hashes);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsABlankVersionLabelAsNone(string? versionLabel)
    {
        Assert.Null(Create(versionLabel: versionLabel).VersionLabel);
    }

    [Fact]
    public void RejectsANegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(sizeInBytes: -1));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("mods/SkyUI.7z")]
    public void RejectsAFileNameThatIsNotASingleSegment(string fileName)
    {
        Assert.Throws<ArgumentException>(() => Create(fileName: fileName));
    }

    [Fact]
    public void RejectsAnUnknownCategory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(category: (ModFileCategory)99));
    }

    [Fact]
    public void RejectsANullHash()
    {
        Assert.Throws<ArgumentException>(() => Create(hashes: [null!]));
    }

    private static ModFile Create(
        string fileName = "SkyUI_5_2_SE.7z",
        string? versionLabel = "5.2SE",
        ModFileCategory category = ModFileCategory.Main,
        long? sizeInBytes = 2_500_000,
        IEnumerable<FileHash>? hashes = null,
        Uri? pageUrl = null,
        ModFileRef? replacedBy = null)
        => new(
            TestData.SkyUiMainFile,
            "Main File",
            fileName,
            versionLabel,
            category,
            sizeInBytes,
            TestData.CreatedAt,
            hashes ?? [Md5],
            pageUrl,
            replacedBy);
}
