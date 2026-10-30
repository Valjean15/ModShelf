using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Updates;

/// <summary>
/// Updates from a mod's files on its site. 50 was replaced by 51 and 51 by 52, all main files. 60 is a main file in no
/// chain, and 61 a later one. 70 and 71 are optional files in no chain, 71 the later.
/// </summary>
public sealed class UpdateFinderTests
{
    private static readonly ModRef SkyUi = TestData.File(TestData.Nexus, "1").Mod;
    private static readonly DateTimeOffset Now = TestData.Now;

    private static readonly IReadOnlyList<ModFile> Site =
    [
        Listed("50", days: -30, replacedBy: "51"),
        Listed("51", days: -20, replacedBy: "52"),
        Listed("52", days: -10),
        Listed("60", days: -25),
        Listed("61", days: -5),
        Listed("70", days: -25, category: ModFileCategory.Optional),
        Listed("71", days: -5, category: ModFileCategory.Optional),
    ];

    [Fact]
    public void AFilesUpdateIsTheNewestOfItsChain()
    {
        LibraryUpdate update = Assert.Single(Find(WithFiles("50")));

        Assert.Equal(("52", "50"), (update.File.Id, update.Replaces?.Id));
        Assert.Equal(("SkyUI 52", "52.7z", "5.2", ModFileCategory.Main), (update.Name, update.FileName, update.VersionLabel, update.Category));
        Assert.Equal((Now.AddDays(-10), Now, new Uri("https://mods.example/1/52")), (update.UploadedAt, update.FoundAt, update.PageUrl));
    }

    [Fact]
    public void ItReplacesTheNewestFileOfTheChainTheLibraryHas()
    {
        LibraryUpdate update = Assert.Single(Find(WithFiles("50", "51")));

        Assert.Equal(("52", "51"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AChainThatEndsInTheLibraryHasNoUpdate()
    {
        Assert.Empty(Find(WithFiles("50", "52")));
    }

    [Fact]
    public void AMainFileNothingReplacedHasTheNewestLaterMainFile()
    {
        LibraryUpdate update = Assert.Single(Find(WithFiles("60")));

        Assert.Equal(("61", "60"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AFileInAChainHasNoUpdateButItsChains()
    {
        Assert.Empty(Find(WithFiles("52")));
    }

    [Fact]
    public void AMainFileInNoChainDoesntTakeAChainsFile()
    {
        ModFile[] site = [Listed("60", days: -25), Listed("51", days: -20, replacedBy: "52"), Listed("52", days: -1)];

        Assert.Empty(Find(WithFiles("60"), site));
    }

    [Fact]
    public void AnUpdateOfTwoMainFilesReplacesTheNewer()
    {
        ModFile[] site = [Listed("62", days: -40), Listed("60", days: -25), Listed("61", days: -5)];

        LibraryUpdate update = Assert.Single(Find(WithFiles("60", "62"), site));

        Assert.Equal(("61", "60"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AnOlderMainFileIsNoUpdate()
    {
        Assert.Empty(Find(WithFiles("61")));
    }

    [Fact]
    public void AMainFileWithANewerOneInTheLibraryHasNoUpdate()
    {
        Assert.Empty(Find(WithFiles("60", "61")));
    }

    [Fact]
    public void AMainFileNothingReplacedHasTheNewestOfSeveralLaterOnes()
    {
        ModFile[] site = [Listed("60", days: -25), Listed("62", days: -15), Listed("61", days: -5)];

        LibraryUpdate update = Assert.Single(Find(WithFiles("60"), site));

        Assert.Equal(("61", "60"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void ALaterOptionalFileIsNoUpdateOfAMainFile()
    {
        ModFile[] site = [Listed("60", days: -25), Listed("71", days: -1, category: ModFileCategory.Optional)];

        Assert.Empty(Find(WithFiles("60"), site));
    }

    [Fact]
    public void TheChainSaysWhichLibraryFileTheUpdateReplaces()
    {
        // Both library files were uploaded at the same time, so only the chain says 81 is the newer.
        ModFile[] site = [Listed("80", days: -40, replacedBy: "81"), Listed("81", days: -40, replacedBy: "82"), Listed("82", days: -1)];

        LibraryUpdate update = Assert.Single(Find(WithFiles("81", "80"), site));

        Assert.Equal(("82", "81"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AnOptionalFileNothingReplacedHasNoUpdate()
    {
        Assert.Empty(Find(WithFiles("70")));
    }

    [Fact]
    public void TheChainCountsForMoreThanAnUploadTime()
    {
        // 52, the newest main file, is both 51's chain's end and newer than 60.
        ModFile[] site = [Listed("51", days: -20, replacedBy: "52"), Listed("52", days: -1), Listed("60", days: -25)];

        LibraryUpdate update = Assert.Single(Find(WithFiles("60", "51"), site));

        Assert.Equal(("52", "51"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AnUpdateTheSiteNoLongerListsIsntOne()
    {
        ModFile[] site = [Listed("50", days: -30, replacedBy: "51"), Listed("60", days: -25)];

        Assert.Empty(Find(WithFiles("50", "60"), site));
    }

    [Fact]
    public void AChainThatGoesRoundEnds()
    {
        ModFile[] site = [Listed("1", days: -2, replacedBy: "2"), Listed("2", days: -1, replacedBy: "1")];

        Assert.Equal(["2"], Find(WithFiles("1"), site).Select(update => update.File.Id));
    }

    [Fact]
    public void ALinkBackToAnOlderArchivedFileIsntFollowed()
    {
        // As Nexus Mods lists some mods: their newest file "replaced" by an older one, which is archived.
        ModFile[] site =
        [
            Listed("79", days: -10, replacedBy: "80", category: ModFileCategory.OldVersion),
            Listed("80", days: -5, replacedBy: "81"),
            Listed("81", days: -35, category: ModFileCategory.Archived),
        ];

        Assert.Empty(Find(WithFiles("80"), site));
    }

    [Fact]
    public void ALinkBackToAnOlderFileIsntFollowed()
    {
        ModFile[] site = [Listed("80", days: -5, replacedBy: "81"), Listed("81", days: -35, category: ModFileCategory.OldVersion)];

        Assert.Empty(Find(WithFiles("80"), site));
    }

    [Fact]
    public void AMainFileWhoseOnlyLinkGoesBackIsInNoChain()
    {
        ModFile[] site = [Listed("80", days: -5, replacedBy: "81"), Listed("81", days: -35, category: ModFileCategory.OldVersion), Listed("82", days: -1)];

        LibraryUpdate update = Assert.Single(Find(WithFiles("80"), site));

        Assert.Equal(("82", "80"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void AnArchivedFileIsNoUpdate()
    {
        // As an author who withdrew a version: both its uploads archived, the one before it the main file again.
        ModFile[] site =
        [
            Listed("90", days: -3, replacedBy: "91"),
            Listed("91", days: -2, replacedBy: "92", category: ModFileCategory.Archived),
            Listed("92", days: -1, category: ModFileCategory.Archived),
        ];

        Assert.Empty(Find(WithFiles("90"), site));
    }

    [Fact]
    public void AChainGoesOnPastAnArchivedFile()
    {
        ModFile[] site =
        [
            Listed("90", days: -3, replacedBy: "91"),
            Listed("91", days: -2, replacedBy: "92", category: ModFileCategory.Archived),
            Listed("92", days: -1, category: ModFileCategory.OldVersion),
        ];

        LibraryUpdate update = Assert.Single(Find(WithFiles("90"), site));

        Assert.Equal(("92", "90"), (update.File.Id, update.Replaces?.Id));
    }

    [Fact]
    public void NoUpdateIsOlderThanAFileTheLibraryHasFurtherOnTheChain()
    {
        ModFile[] site =
        [
            Listed("90", days: -3, replacedBy: "91"),
            Listed("91", days: -2, replacedBy: "92"),
            Listed("92", days: -1, category: ModFileCategory.Archived),
        ];

        Assert.Empty(Find(WithFiles("90", "92"), site));
    }

    [Fact]
    public void AWatchedModGetsTheMainFilesSinceItsLastCheck()
    {
        IReadOnlyList<LibraryUpdate> updates = Find(Watched(), since: Now.AddDays(-12));

        Assert.Equal(["61", "52"], updates.Select(update => update.File.Id));
        Assert.All(updates, update => Assert.Null(update.Replaces));
    }

    [Fact]
    public void AWatchedModKeepsWhatEarlierChecksFoundWhileTheSiteListsIt()
    {
        LibraryMod mod = Watched().WithUpdates([Earlier("50"), Earlier("99")]);

        IReadOnlyList<LibraryUpdate> updates = Find(mod, since: Now.AddDays(-1));

        Assert.Equal(["50"], updates.Select(update => update.File.Id));
    }

    [Fact]
    public void AModWithOnlyArchivedFilesIsOnlyWatched()
    {
        LibraryMod mod = Watched().WithFile(Stored("50", "SkyUI/_archive/50.7z"));

        IReadOnlyList<LibraryUpdate> updates = Find(mod, since: Now.AddDays(-12));

        Assert.Equal(["61", "52"], updates.Select(update => update.File.Id));
    }

    [Fact]
    public void ArchivedFilesDontAskForUpdates()
    {
        LibraryMod mod = WithFiles("52").WithFile(Stored("60", "SkyUI/_archive/60.7z"));

        Assert.Empty(Find(mod));
    }

    [Fact]
    public void ListsTheNewestFirst()
    {
        IReadOnlyList<LibraryUpdate> updates = Find(WithFiles("50", "60"));

        Assert.Equal(["61", "52"], updates.Select(update => update.File.Id));
    }

    private static IReadOnlyList<LibraryUpdate> Find(LibraryMod mod, IReadOnlyList<ModFile>? site = null, DateTimeOffset? since = null)
        => UpdateFinder.Find(mod, site ?? Site, since ?? Now.AddDays(-1), Now);

    private static ModFileRef Ref(string id) => new(SkyUi, id);

    private static ModFile Listed(string id, int days, string? replacedBy = null, ModFileCategory category = ModFileCategory.Main)
        => new(
            Ref(id),
            $"SkyUI {id}",
            $"{id}.7z",
            id.Length == 2 ? $"{id[0]}.{id[1]}" : id,
            category,
            100,
            Now.AddDays(days),
            [],
            new Uri($"https://mods.example/1/{id}"),
            replacedBy is null ? null : Ref(replacedBy));

    private static LibraryFile Stored(string id, string? path = null)
    {
        ModFile listed = Site.FirstOrDefault(file => file.Reference.Id == id) ?? Listed(id, days: -40);
        return new LibraryFile(Ref(id), listed.Name, listed.VersionLabel, listed.Category, listed.UploadedAt, path ?? $"SkyUI/{id}.7z", 100, Now);
    }

    private static LibraryUpdate Earlier(string id)
        => new(Ref(id), $"SkyUI {id}", $"{id}.7z", null, ModFileCategory.Main, Now.AddDays(-30), null, Now.AddDays(-2));

    private static LibraryMod Watched() => LibraryMod.Watch(SkyUi, "SkyUI", null, null, Now.AddDays(-100));

    private static LibraryMod WithFiles(params string[] ids)
    {
        LibraryMod mod = Watched();
        foreach (string id in ids)
        {
            mod = mod.WithFile(Stored(id));
        }

        return mod;
    }
}
