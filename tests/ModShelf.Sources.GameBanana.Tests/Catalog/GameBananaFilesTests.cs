using ModShelf.Domain.Mods;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests.Catalog;

/// <summary>
/// A mod's files side by side, as GameBanana lists them: archives main, files it can't read miscellaneous, and older
/// versions kept apart replaced by the newer archive of the same name.
/// </summary>
public sealed class GameBananaFilesTests
{
    private static readonly ModRef Mod = GameBananaHarness.Mod;

    [Theory]
    [InlineData("p5r_female_protagonist1_2_27.7z", "prfemaleprotagonist")]
    [InlineData("p5r_female_protagonist1_2_28.7z", "prfemaleprotagonist")]
    [InlineData("p5r_female_protagonist_-_english_voiceover1_1_11.7z", "prfemaleprotagonistenglishvoiceover")]
    [InlineData("nahida_redesign_pale_187ca.zip", "nahidaredesignpale")]
    [InlineData("nahida_redesign_tan_1a88f.zip", "nahidaredesigntan")]
    [InlineData("Mod V1.2 Final.RAR", "modvfinal")]
    [InlineData("12345.zip", "")]
    public void AStemIsTheNameWithItsVersionLeftOut(string fileName, string stem)
    {
        Assert.Equal(stem, GameBananaFiles.Stem(fileName));
    }

    [Fact]
    public void ArchivesAreMainFilesAndTheRestMiscellaneous()
    {
        IReadOnlyList<ModFile> files = GameBananaFiles.From(Mod, Profile(
            File(1, "variant_pale.zip", 100),
            File(2, "variant_tan.zip", 200),
            File(3, "releasemetadata_36aa9.json", 300, hasContents: false),
            File(4, "readme.txt", 400, hasContents: null)));

        Assert.Equal(
            [("1", ModFileCategory.Main), ("2", ModFileCategory.Main), ("3", ModFileCategory.Miscellaneous), ("4", ModFileCategory.Main)],
            files.Select(file => (file.Reference.Id, file.Category)));
        Assert.All(files, file => Assert.Null(file.ReplacedBy));
    }

    [Fact]
    public void AnOlderVersionIsReplacedByTheNewestLaterArchiveOfItsName()
    {
        IReadOnlyList<ModFile> files = GameBananaFiles.From(Mod, Profile(
            [File(5, "mod1_3.7z", 500), File(6, "mod1_4.7z", 600), File(7, "mod_addon1_4.7z", 700), File(8, "mod1_5_metadata.json", 800, hasContents: false)],
            [File(2, "mod1_1.7z", 200), File(9, "mod_addon1_0.7z", 900), File(3, "other.7z", 300)]));

        Assert.Equal(
            [("2", ModFileCategory.OldVersion, "6"), ("9", ModFileCategory.OldVersion, null), ("3", ModFileCategory.OldVersion, null)],
            files.Where(file => file.Category == ModFileCategory.OldVersion).Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
    }

    [Fact]
    public void AFileMarkedArchivedAmongTheCurrentIsAnOldVersionOnce()
    {
        IReadOnlyList<ModFile> files = GameBananaFiles.From(Mod, Profile(
            [File(1, "mod1_0.7z", 100, archived: true), File(2, "mod1_1.7z", 200)],
            [File(1, "mod1_0.7z", 100, archived: true)]));

        Assert.Equal([("2", ModFileCategory.Main, null), ("1", ModFileCategory.OldVersion, "2")], files.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
    }

    [Fact]
    public void AFileSaysWhatGameBananaSaysOfIt()
    {
        GameBananaFile listed = File(1722096, "p5r_female_protagonist1_2_28.7z", 1780795077) with
        {
            Size = 218808279,
            Md5 = "29EB8BBFBB91CA848FB84D61D355D493",
            Version = "1.2.28",
            Description = " Main FeMC mod, DOWNLOAD THIS ONE ",
        };

        ModFile file = Assert.Single(GameBananaFiles.From(Mod, Profile(listed)));

        Assert.Equal(
            ("Main FeMC mod, DOWNLOAD THIS ONE", "p5r_female_protagonist1_2_28.7z", "1.2.28", 218808279L, DateTimeOffset.FromUnixTimeSeconds(1780795077)),
            (file.Name, file.FileName, file.VersionLabel, file.SizeInBytes, file.UploadedAt));
        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, "29eb8bbfbb91ca848fb84d61d355d493"), Assert.Single(file.Hashes));
        Assert.Equal(new Uri("https://gamebanana.com/mmdl/1722096,Mod,440088"), file.PageUrl);
    }

    [Fact]
    public void AFileWithoutADescriptionIsNamedByItsFileAndAMalformedChecksumIsLeftOut()
    {
        ModFile file = Assert.Single(GameBananaFiles.From(Mod, Profile(File(1, "mod.7z", 100) with { Md5 = "not-a-checksum" })));

        Assert.Equal("mod.7z", file.Name);
        Assert.Empty(file.Hashes);
    }

    [Theory]
    [InlineData("not-a-checksum")]
    [InlineData("zz8bbfbb91ca848fb84d61d355d493zz")]
    [InlineData("")]
    public void AChecksumThatIsntAnMd5IsLeftOut(string md5)
    {
        GameBananaFile listed = File(1, "mod.7z", 100) with { Md5 = md5 };

        Assert.Empty(Assert.Single(GameBananaFiles.From(Mod, Profile(listed))).Hashes);
        Assert.Null(GameBananaFiles.Hash(listed));
    }

    [Fact]
    public void AModsDownloadIsTheArchiveUploadedLast()
    {
        GameBananaMod profile = Profile(
            [File(1, "mod1_0.7z", 100), File(2, "mod_metadata.json", 300, hasContents: false), File(3, "mod_addon.7z", 200)],
            [File(4, "mod0_9.7z", 400)]);

        Assert.Equal(3, GameBananaFiles.MainDownload(profile)?.Id);
    }

    [Fact]
    public void AModWithoutArchivesDownloadsTheFileUploadedLast()
    {
        Assert.Equal(2, GameBananaFiles.MainDownload(Profile(File(1, "a.json", 100, hasContents: false), File(2, "b.json", 200, hasContents: false)))?.Id);
        Assert.Null(GameBananaFiles.MainDownload(Profile()));
    }

    [Fact]
    public void FindsAFileCurrentOrArchived()
    {
        GameBananaMod profile = Profile([File(1, "a.7z", 100)], [File(2, "b.7z", 50)]);

        Assert.Equal((1L, 2L), (GameBananaFiles.Find(profile, "1")?.Id ?? 0, GameBananaFiles.Find(profile, "2")?.Id ?? 0));
        Assert.Null(GameBananaFiles.Find(profile, "3"));
    }

    private static GameBananaFile File(long id, string name, long added, bool? hasContents = true, bool archived = false)
        => new() { Id = id, FileName = name, DateAdded = added, HasContents = hasContents, IsArchived = archived };

    private static GameBananaMod Profile(params GameBananaFile[] files) => Profile(files, []);

    private static GameBananaMod Profile(GameBananaFile[] files, GameBananaFile[] archived)
        => new() { Id = 440088, Files = files, ArchivedFiles = archived };
}
