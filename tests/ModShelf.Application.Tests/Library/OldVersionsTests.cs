using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Library;

public sealed class OldVersionsTests
{
    private static readonly ModRef SkyUi = TestData.File(TestData.Nexus, "1").Mod;

    // 50 was replaced by 51, 51 by 52 and 52 by 53; 60 by 61. 70 replaced nothing.
    private static readonly IReadOnlyList<ModFile> Site =
    [
        Listed("50", replacedBy: "51"),
        Listed("51", replacedBy: "52"),
        Listed("52", replacedBy: "53"),
        Listed("53"),
        Listed("60", replacedBy: "61"),
        Listed("61"),
        Listed("70"),
    ];

    [Fact]
    public void AnOldVersionIsAFileTheNewerOneReplacedThroughAnyVersionsBetween()
    {
        LibraryMod mod = Mod("50", "52", "60", "70");

        IReadOnlyList<LibraryFile> older = OldVersions.ReplacedBy(mod, File("53"), Site);

        Assert.Equal(["52", "50"], older.Select(file => file.File.Id));
    }

    [Fact]
    public void ANewerVersionIsntAnOldOne()
    {
        Assert.Empty(OldVersions.ReplacedBy(Mod("51", "52", "53"), File("50"), Site));
        Assert.Equal(["50"], OldVersions.ReplacedBy(Mod("50", "52", "53"), File("51"), Site).Select(file => file.File.Id));
    }

    [Fact]
    public void AFileTheSiteNoLongerListsIsntAnOldVersion()
    {
        LibraryMod mod = Mod("40", "52");

        Assert.Equal(["52"], OldVersions.ReplacedBy(mod, File("53"), Site).Select(file => file.File.Id));
    }

    [Fact]
    public void AChainThatGoesRoundEnds()
    {
        ModFile[] site = [Listed("1", replacedBy: "2"), Listed("2", replacedBy: "1"), Listed("3")];

        Assert.Empty(OldVersions.ReplacedBy(Mod("1", "2"), File("3"), site));
    }

    [Fact]
    public void ANewerFileIsntAnOldVersionOfAnOlderOneTheSiteLinksBackTo()
    {
        // As Nexus Mods lists some mods: their newest file "replaced" by an older one, which is archived.
        ModFile[] site = [Listed("79", replacedBy: "80", days: -10), Listed("80", replacedBy: "81", days: -5), Listed("81", days: -35)];

        Assert.Empty(OldVersions.ReplacedBy(Mod("79", "80"), File("81"), site));
        Assert.Empty(OldVersions.Plan(Mod("79", "80"), File("81"), site, OldVersionRule.KeepLatest(1)).Delete);
    }

    [Fact]
    public void KeepingAllDoesNothing()
    {
        Assert.Equal(OldVersionPlan.None, OldVersions.Plan(Mod("50", "51", "52"), File("53"), Site, OldVersionRule.KeepAll));
    }

    [Theory]
    [InlineData(1, new[] { "52", "51", "50" })]
    [InlineData(2, new[] { "51", "50" })]
    [InlineData(3, new[] { "50" })]
    [InlineData(4, new string[0])]
    public void KeepingTheLatestDeletesTheOlderVersions(int count, string[] deleted)
    {
        OldVersionPlan plan = OldVersions.Plan(Mod("50", "51", "52", "70"), File("53"), Site, OldVersionRule.KeepLatest(count));

        Assert.Empty(plan.Archive);
        Assert.Equal(deleted, plan.Delete.Select(file => file.File.Id));
    }

    [Fact]
    public void ArchivedVersionsCountAsVersionsKept()
    {
        LibraryMod mod = Mod(("51", "mods/SkyUI/_archive/51.7z"), ("52", "mods/SkyUI/52.7z"));

        OldVersionPlan plan = OldVersions.Plan(mod, File("53"), Site, OldVersionRule.KeepLatest(2));

        Assert.Equal(["51"], plan.Delete.Select(file => file.File.Id));
    }

    [Fact]
    public void ArchivingLeavesFilesArchivedAlready()
    {
        LibraryMod mod = Mod(("50", "mods/SkyUI/_ARCHIVE/50.7z"), ("51", "mods/SkyUI/51.7z"), ("70", "mods/SkyUI/70.7z"));

        OldVersionPlan plan = OldVersions.Plan(mod, File("53"), Site, OldVersionRule.Archive);

        Assert.Equal(["51"], plan.Archive.Select(file => file.File.Id));
        Assert.Empty(plan.Delete);
    }

    [Theory]
    [InlineData("mods/SkyUI/_archive/50.7z", true)]
    [InlineData("_archive/50.7z", true)]
    [InlineData("mods/_archive/SkyUI/50.7z", false)]
    [InlineData("mods/SkyUI/50.7z", false)]
    [InlineData("_archive", false)]
    public void TellsWhetherAFileIsArchived(string relativePath, bool archived)
    {
        Assert.Equal(archived, OldVersions.IsArchived(relativePath));
    }

    [Theory]
    [InlineData("mods/SkyUI/SkyUI 5.0.7z", "mods/SkyUI/_archive/SkyUI 5.0.7z")]
    [InlineData("SkyUI 5.0.7z", "_archive/SkyUI 5.0.7z")]
    public void ArchivesIntoTheFilesOwnFolder(string relativePath, string archivePath)
    {
        Assert.Equal(archivePath, OldVersions.ArchivePath(relativePath, "SkyUI 5.0.7z"));
    }

    private static ModFileRef File(string id) => new(SkyUi, id);

    private static ModFile Listed(string id, string? replacedBy = null, int days = 0)
        => new(File(id), $"SkyUI {id}", $"{id}.7z", null, ModFileCategory.Main, null, TestData.Now.AddDays(days), [], replacedBy: replacedBy is null ? null : File(replacedBy));

    private static LibraryMod Mod(params string[] ids) => Mod([.. ids.Select(id => (id, $"mods/SkyUI/{id}.7z"))]);

    private static LibraryMod Mod(params (string Id, string Path)[] files)
    {
        LibraryMod mod = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now);
        foreach ((string id, string path) in files)
        {
            mod = mod.WithFile(new LibraryFile(File(id), $"SkyUI {id}", null, ModFileCategory.Main, TestData.Now, path, 1, TestData.Now));
        }

        return mod;
    }
}
