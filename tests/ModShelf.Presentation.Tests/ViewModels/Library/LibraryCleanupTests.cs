using ModShelf.Application.Cleanup;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Free up space… in the Library: the open game's files, or every game's, listed in the dialog by group, the archived
/// old versions and the duplicates chosen to go and each file that may be the only copy left kept, saying why; then the
/// chosen files deleted once the user says so (ADR 0104). Each file is a megabyte.
/// </summary>
public sealed class LibraryCleanupTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Survival = TestData.Mod("3863");

    private readonly LibraryHarness _harness = new();
    private readonly CultureScope _culture = new("en-US");

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose()
    {
        _harness.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task OnAGamesPageItListsThatGamesFilesAndOtherwiseEveryGames()
    {
        await StartAsync();

        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);

        Assert.Equal([null, SkyUi.Game], _harness.Cleanup.Planned);
        Assert.Equal(["Free up space", "Free up space in Skyrim Special Edition"], _harness.Dialogs.FreeingSpace.Select(freeing => freeing.Title));
    }

    [Fact]
    public async Task ItShowsEachGroupWithItsFilesTheArchivedAndTheDuplicatesChosen()
    {
        await StartAsync();
        AddEntries();

        FreeUpSpaceViewModel freeing = await ListAsync();

        Assert.Equal(FreeUpSpaceStage.Listed, freeing.Stage);
        Assert.Equal(
            [
                "Archived old versions: 1 file can go, 1.00 MB. 1 is kept.",
                "  [x] SkyUI 5.1.7z, SkyUI · SkyUI/_archive, 1.00 MB",
                "  [-] SkyUI 5.0.7z, SkyUI · SkyUI/_archive, 1.00 MB: Kept: Nexus Mods no longer offers it, so this may be the only copy.",
                "Duplicates: 1 file can go, 1.00 MB.",
                "  [x] SkyUI copy.7z, SkyUI · SkyUI, 1.00 MB: Same as SkyUI/SkyUI 5.2.7z, of SkyUI, which stays.",
                "Files of mods not watched: 2 files can go, 2.00 MB.",
                "  [ ] Survival 1.7z, SkyUI Survival · Survival, 1.00 MB",
                "  [ ] Survival 2.7z, SkyUI Survival · Survival, 1.00 MB",
                "Files that match nothing: 1 file can go, 1.00 MB. 2 are kept.",
                "  [ ] Readme.txt, SkyUI · SkyUI, 1.00 MB",
                "  [-] SkyUI 5.3.7z, SkyUI · SkyUI, 1.00 MB: Kept: it's SkyUI 5.3 on Nexus Mods. Rescan adds it to the library.",
                "  [-] Notes.txt, SkyUI Survival · Survival, 1.00 MB: Kept: Nexus Mods couldn't be asked whether it still offers it. Slow down.",
            ],
            Shown(freeing));
        Assert.Equal("5 files can go, 5.00 MB. 3 files are kept, each saying why.", freeing.SummaryText);
        Assert.Equal([true, true, false, false], freeing.Groups.Select(group => group.IsSelected));
        Assert.Equal("Delete selected (2.00 MB)…", freeing.DeleteText);
        Assert.True(freeing.DeleteCommand.CanExecute(null));
        Assert.True(freeing.ShowsRows);
    }

    [Fact]
    public async Task ChoosingAGroupOrAFileChangesWhatGoesAndItsSize()
    {
        await StartAsync();
        AddEntries();
        FreeUpSpaceViewModel freeing = await ListAsync();
        CleanupGroupRow unwatched = freeing.Groups[2];
        CleanupGroupRow unmatched = freeing.Groups[3];

        unwatched.IsSelected = true;
        string all = freeing.DeleteText;
        unwatched.Files[0].IsSelected = false;
        bool? some = unwatched.IsSelected;
        unmatched.IsSelected = true;
        (bool? unmatchedChosen, string withUnmatched) = (unmatched.IsSelected, freeing.DeleteText);
        freeing.Groups[0].IsSelected = false;
        freeing.Groups[1].IsSelected = false;
        unwatched.IsSelected = false;
        unmatched.IsSelected = false;

        Assert.Equal("Delete selected (4.00 MB)…", all);
        Assert.Null(some);
        Assert.Equal((true, "Delete selected (4.00 MB)…"), (unmatchedChosen, withUnmatched));
        Assert.Equal(("Delete selected…", false), (freeing.DeleteText, freeing.DeleteCommand.CanExecute(null)));
        Assert.Throws<InvalidOperationException>(() => unmatched.Files[1].IsSelected = true);
        Assert.False(unmatched.Files[1].IsSelected);
    }

    [Fact]
    public async Task DeletingAsksFirstThenDeletesTheChosenFilesAndSaysWhatItDid()
    {
        await StartAsync();
        AddEntries();
        _harness.Cleanup.Left["SkyUI copy.7z"] = "Another program has it open.";
        _harness.Dialogs.Confirm.Enqueue(false);
        _harness.Dialogs.Confirm.Enqueue(true);
        FreeUpSpaceViewModel? shown = null;
        List<FreeUpSpaceStage> stages = [];
        _harness.Dialogs.InFreeUpSpace = async freeing =>
        {
            shown = freeing;
            await freeing.Listing;
            await freeing.DeleteCommand.ExecuteAsync(null);
            stages.Add(freeing.Stage);
            freeing.Groups[2].IsSelected = true;
            await freeing.DeleteCommand.ExecuteAsync(null);
        };

        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);

        FreeUpSpaceViewModel freeing = Assert.IsType<FreeUpSpaceViewModel>(shown);
        Assert.Equal([FreeUpSpaceStage.Listed], stages);
        Assert.Equal(
            [
                ("Delete the selected files?", "Delete 2 files, 2.00 MB, for good? They don't go to the trash.", "Delete"),
                ("Delete the selected files?", "Delete 4 files, 4.00 MB, for good? They don't go to the trash.", "Delete"),
            ],
            _harness.Dialogs.Confirmations);
        Assert.Equal(
            ["SkyUI 5.1.7z", "SkyUI copy.7z", "Survival 1.7z", "Survival 2.7z"],
            Assert.Single(_harness.Cleanup.Deletions).Select(entry => entry.FileName));
        Assert.Equal((FreeUpSpaceStage.Done, "Deleted 3 files, freeing 3.00 MB.", "OK"), (freeing.Stage, freeing.ResultText, freeing.CloseText));
        Assert.Equal(["SkyUI copy.7z: Another program has it open."], freeing.Problems);
        Assert.True(freeing.HasProblems);
        Assert.False(freeing.ShowsRows);
    }

    [Fact]
    public async Task StopEndsListingOrDeletingAndTheDialogStaysToSaySo()
    {
        await StartAsync();
        AddEntries();
        List<(bool CanClose, FreeUpSpaceStage Stage, string? Result)> seen = [];
        _harness.Cleanup.HoldPlanning = new TaskCompletionSource();
        _harness.Dialogs.InFreeUpSpace = async freeing =>
        {
            seen.Add((freeing.CanClose, freeing.Stage, freeing.ResultText));
            freeing.StopCommand.Execute(null);
            await freeing.Listing;
            seen.Add((freeing.CanClose, freeing.Stage, freeing.ResultText));
        };

        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);
        _harness.Cleanup.HoldPlanning = null;
        _harness.Cleanup.HoldDeleting = new TaskCompletionSource();
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Dialogs.InFreeUpSpace = async freeing =>
        {
            await freeing.Listing;
            Task deleting = freeing.DeleteCommand.ExecuteAsync(null);
            seen.Add((freeing.CanClose, freeing.Stage, freeing.ResultText));
            freeing.StopCommand.Execute(null);
            await deleting;
            seen.Add((freeing.CanClose, freeing.Stage, freeing.ResultText));
        };
        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                (false, FreeUpSpaceStage.Listing, null),
                (true, FreeUpSpaceStage.Done, "Stopped. Nothing was deleted."),
                (false, FreeUpSpaceStage.Deleting, null),
                (true, FreeUpSpaceStage.Done, "Stopped. The files deleted so far are gone; the rest are where they were."),
            ],
            seen);
    }

    [Fact]
    public async Task NothingToFreeUpOrAFailureIsSaid()
    {
        await StartAsync();

        FreeUpSpaceViewModel nothing = await ListAsync();
        _harness.Cleanup.PlanFailure = new InvalidDataException("The settings in settings.json can't be read.");
        FreeUpSpaceViewModel unreadable = await ListAsync();
        _harness.Cleanup.PlanFailure = new InvalidCastException("A bug.");
        FreeUpSpaceViewModel failed = await ListAsync();
        _harness.Cleanup.PlanFailure = null;
        AddEntries();
        _harness.Cleanup.DeleteFailure = new InvalidOperationException("A download couldn't be recorded. Try again later.");
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Dialogs.InFreeUpSpace = async freeing =>
        {
            await freeing.Listing;
            await freeing.DeleteCommand.ExecuteAsync(null);
        };
        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);

        Assert.Equal(
            (FreeUpSpaceStage.Listed, "Nothing to free up: there are no archived old versions, duplicates, files of mods not watched or files that match nothing.", false, false),
            (nothing.Stage, nothing.SummaryText, nothing.ShowsRows, nothing.DeleteCommand.CanExecute(null)));
        Assert.Equal((FreeUpSpaceStage.Done, "The settings in settings.json can't be read."), (unreadable.Stage, unreadable.ResultText));
        Assert.Equal((FreeUpSpaceStage.Done, "Freeing up space failed. The log says why."), (failed.Stage, failed.ResultText));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Listing what could go from the library failed", StringComparison.Ordinal));
        Assert.Equal("A download couldn't be recorded. Try again later.", _harness.Dialogs.FreeingSpace[^1].ResultText);
    }

    private static CleanupEntry Entry(CleanupGroup group, ModRef mod, string modName, string relativePath, LibraryFile? file = null)
        => new(group, LibraryPaths.Resolve(TestData.LibraryRoot, relativePath), relativePath, Megabyte, mod, modName, file);

    private static LibraryFile File(ModRef mod, string id, string relativePath)
        => new(new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, TestData.Now, relativePath, Megabyte, TestData.Now);

    private static IEnumerable<string> Shown(FreeUpSpaceViewModel freeing)
        => freeing.Rows.Select(row => row switch
        {
            CleanupGroupRow group => $"{group.Heading}: {group.TotalText}",
            CleanupFileRow file => $"  [{(file.IsSelected ? "x" : file.CanGo ? " " : "-")}] {file.FileName}, {file.Where}, {file.SizeText}{(file.Said is { } said ? $": {said}" : string.Empty)}",
            _ => row.ToString() ?? string.Empty,
        });

    /// <summary>
    /// What a plan lists: an archived old version that can go and one kept; a duplicate; two files of a mod not watched;
    /// and a file that matches nothing, beside two kept.
    /// </summary>
    private void AddEntries()
    {
        LibraryFile copy = File(SkyUi, "2", "SkyUI/SkyUI 5.2.7z");
        _harness.Cleanup.Entries.AddRange(
        [
            Entry(CleanupGroup.Archived, SkyUi, "SkyUI", "SkyUI/_archive/SkyUI 5.1.7z", File(SkyUi, "1", "SkyUI/_archive/SkyUI 5.1.7z")),
            Entry(CleanupGroup.Archived, SkyUi, "SkyUI", "SkyUI/_archive/SkyUI 5.0.7z", File(SkyUi, "0", "SkyUI/_archive/SkyUI 5.0.7z")) with { Kept = KeptBecause.NotOffered },
            Entry(CleanupGroup.Duplicates, SkyUi, "SkyUI", "SkyUI/SkyUI copy.7z", File(SkyUi, "3", "SkyUI/SkyUI copy.7z"))
                with { SameAs = new CleanupCopy(LibraryPaths.Resolve(TestData.LibraryRoot, copy.RelativePath), SkyUi, "SkyUI", copy) },
            Entry(CleanupGroup.Unwatched, Survival, "SkyUI Survival", "Survival/Survival 1.7z", File(Survival, "1", "Survival/Survival 1.7z")),
            Entry(CleanupGroup.Unwatched, Survival, "SkyUI Survival", "Survival/Survival 2.7z", File(Survival, "2", "Survival/Survival 2.7z")),
            Entry(CleanupGroup.Unmatched, SkyUi, "SkyUI", "SkyUI/Readme.txt"),
            Entry(CleanupGroup.Unmatched, SkyUi, "SkyUI", "SkyUI/SkyUI 5.3.7z") with { Kept = KeptBecause.MatchesSiteFile, KeptDetail = "SkyUI 5.3" },
            Entry(CleanupGroup.Unmatched, Survival, "SkyUI Survival", "Survival/Notes.txt") with { Kept = KeptBecause.NotChecked, KeptDetail = "Slow down." },
        ]);
    }

    /// <summary>
    /// Shows the dialog, which the user closes once it has listed what could go.
    /// </summary>
    private async Task<FreeUpSpaceViewModel> ListAsync()
    {
        await Screen.FreeUpSpaceCommand.ExecuteAsync(null);
        return _harness.Dialogs.FreeingSpace[^1];
    }

    private Task StartAsync()
        => _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")));
}
