using ModShelf.Application.Archives;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Import archives… in the Library: the game open, or one chosen among the sites' games, then the folder; the archives
/// listed and shown in the dialog, which identifies them, asking first when the site's requests look too few, then
/// copies them in. Nothing in the folder changes.
/// </summary>
public sealed class LibraryArchivesTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "MO2", "downloads");

    private readonly LibraryHarness _harness = new();
    private readonly CultureScope _culture = new("en-US");

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose()
    {
        _harness.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task OnAGamesPageItImportsThatGamesArchivesFromTheFolderChosen()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        Assert.Equal(["Choose the folder of Skyrim Special Edition's archives"], _harness.Dialogs.FolderPickers);
        Assert.Equal([(Folder, SkyUi.Game)], _harness.Archives.Scanned);
        ImportArchivesViewModel importing = Assert.Single(_harness.Dialogs.ImportingArchives);
        Assert.Equal("Import archives of Skyrim Special Edition", importing.Title);
        Assert.Equal($"2 archives in {Folder}. Identifying them reads each one, and takes about 3 requests to Nexus Mods.", importing.ScanText);
        Assert.Empty(_harness.Dialogs.AddingGames);
        Assert.Equal(0, _harness.Archives.Identifications);
    }

    [Fact]
    public async Task WithNoGameOpenItAsksWhichGameAmongTheSites()
    {
        _harness.Lookup.Games[TestData.Nexus] = [new GameInfo(new GameRef(TestData.Nexus, "starfield"), "Starfield")];
        await StartAsync();
        _harness.Dialogs.InAddGame = PickStarfieldAsync;
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        AddGameViewModel choosing = Assert.Single(_harness.Dialogs.AddingGames);
        Assert.Equal(("Which game are the archives of?", "Choose"), (choosing.Title, choosing.ConfirmText));
        Assert.Equal(["Choose the folder of Starfield's archives"], _harness.Dialogs.FolderPickers);
        Assert.Equal([(Folder, new GameRef(TestData.Nexus, "starfield"))], _harness.Archives.Scanned);
        Assert.Equal("Import archives of Starfield", Assert.Single(_harness.Dialogs.ImportingArchives).Title);
    }

    [Fact]
    public async Task AKeyTypedIsntAGameAndNoFolderIsNothing()
    {
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue("fallout4");

        await Screen.ImportArchivesCommand.ExecuteAsync(null);
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        Assert.Equal([("Which game are the archives of?", "Choose the game from the list, so ModShelf knows which site its archives are from.")], _harness.Dialogs.Messages);
        Assert.Single(_harness.Dialogs.FolderPickers);
        Assert.Empty(_harness.Archives.Scanned);
        Assert.Empty(_harness.Dialogs.ImportingArchives);
    }

    [Fact]
    public async Task AFolderItCantImportFromOrWithoutArchivesIsSaid()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Archives.ScanFailure = new ArgumentException("That folder is in the library.", "folder");

        await Screen.ImportArchivesCommand.ExecuteAsync(null);
        _harness.Archives.ScanFailure = null;
        _harness.Archives.Names.Clear();
        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                ("Can't import from this folder", "That folder is in the library."),
                ("No archives here", $"{Folder} has no archives: .7z, .zip or .rar files. Its subfolders aren't read."),
            ],
            _harness.Dialogs.Messages);
        Assert.Empty(_harness.Dialogs.ImportingArchives);
    }

    [Fact]
    public async Task TheDialogIdentifiesThenCopiesSayingWhatEachIsAndWhatItDid()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Archives.Names.AddRange(["Same.7z", "Mystery.7z"]);
        _harness.Archives.Identified = scan => new ArchivePlan(
            scan.Folder,
            scan.Game,
            [
                new PlannedArchive(scan.Archives[0].Path, 100, ArchiveState.Identified, ArchiveClue.Notes, new ModFileRef(SkyUi, "35407"), "SkyUI", "SkyUI 5.2"),
                new PlannedArchive(scan.Archives[1].Path, 100, ArchiveState.InLibrary, ArchiveClue.Name, new ModFileRef(TestData.Mod("30379"), "1"), "SKSE", "SKSE 2.2"),
                new PlannedArchive(scan.Archives[2].Path, 100, ArchiveState.Duplicate, Why: "It's the same as SkyUI.7z."),
                new PlannedArchive(scan.Archives[3].Path, 100, ArchiveState.Unknown, Why: "Nexus Mods has no file of the game with its content."),
            ]);
        _harness.Archives.Imported = plan => new ArchiveImportResult([], 0, 0, [new ArchiveProblem(plan.ToImport[0].Path, "There isn't room for it on the library's drive.")]);
        ImportArchivesViewModel? shown = null;
        List<(string Rows, string Facts, bool CanClose)> seen = [];
        _harness.Dialogs.InImportArchives = async importing =>
        {
            shown = importing;
            await importing.IdentifyCommand.ExecuteAsync(null);
            seen.Add((string.Join(" | ", importing.Rows.Select(row => $"{row.Name}: {row.Said}{(row.IsCopied ? " (copied)" : string.Empty)}")), string.Join(" | ", importing.Facts.Select(fact => $"{fact.Label}: {fact.Value}")), importing.CanClose));
            await importing.ImportCommand.ExecuteAsync(null);
        };

        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        ImportArchivesViewModel importing = Assert.IsType<ImportArchivesViewModel>(shown);
        Assert.Equal(
            ("SkyUI.7z: SkyUI, SkyUI 5.2: by its Mod Organizer note (copied) | SKSE.7z: In the library already, as SKSE, SKSE 2.2 | Same.7z: It's the same as SkyUI.7z. | Mystery.7z: Unknown: Nexus Mods has no file of the game with its content.",
                "To copy: 1 archive | In the library: 1 | Copies of others here: 1 | Unknown: 1",
                true),
            Assert.Single(seen));
        Assert.Equal((ImportArchivesStage.Done, "Copied 0 archives into the library."), (importing.Stage, importing.ResultText));
        Assert.Equal(["SkyUI.7z: There isn't room for it on the library's drive."], importing.Problems);
        Assert.True(importing.HasProblems);
        Assert.Equal([1], _harness.Archives.Imports.Select(plan => plan.ToImport.Count));
    }

    [Fact]
    public async Task ItAsksFirstWhenTheSitesRequestsLookTooFew()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Archives.Requests = 500;
        DateTimeOffset now = _harness.Time.GetUtcNow();
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 500, 120, now.AddMinutes(30))]);
        _harness.Dialogs.Confirm.Enqueue(false);
        _harness.Dialogs.InImportArchives = importing => importing.IdentifyCommand.ExecuteAsync(null);

        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        Assert.Contains("500", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Archives.Identifications);
        Assert.Equal(ImportArchivesStage.Scanned, Assert.Single(_harness.Dialogs.ImportingArchives).Stage);
    }

    [Fact]
    public async Task StopEndsIdentifyingOrCopyingAndTheDialogStaysToSaySo()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Dialogs.Folders.Enqueue(Folder);
        List<(bool CanClose, ImportArchivesStage Stage, string? Result)> seen = [];
        _harness.Archives.HoldIdentifying = new TaskCompletionSource();
        _harness.Dialogs.InImportArchives = async importing =>
        {
            Task identifying = importing.IdentifyCommand.ExecuteAsync(null);
            seen.Add((importing.CanClose, importing.Stage, importing.ResultText));
            importing.StopCommand.Execute(null);
            await identifying;
            seen.Add((importing.CanClose, importing.Stage, importing.ResultText));
        };

        await Screen.ImportArchivesCommand.ExecuteAsync(null);
        _harness.Archives.HoldIdentifying = null;
        _harness.Archives.HoldImporting = new TaskCompletionSource();
        _harness.Dialogs.InImportArchives = async importing =>
        {
            await importing.IdentifyCommand.ExecuteAsync(null);
            Task copying = importing.ImportCommand.ExecuteAsync(null);
            seen.Add((importing.CanClose, importing.Stage, importing.ResultText));
            importing.StopCommand.Execute(null);
            await copying;
            seen.Add((importing.CanClose, importing.Stage, importing.ResultText));
        };
        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                (false, ImportArchivesStage.Identifying, null),
                (true, ImportArchivesStage.Done, "Stopped. Nothing was copied."),
                (false, ImportArchivesStage.Importing, null),
                (true, ImportArchivesStage.Done, "Stopped. What was copied stays in the library; import the folder again to go on."),
            ],
            seen);
    }

    [Fact]
    public async Task ASiteThatCantLookUpArchivesIsSaid()
    {
        await StartAsync();
        _ = _harness.Row(TestData.LibraryMod(SkyUi, "any"));
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Archives.CanLookUp = false;
        _harness.Archives.Requests = 1;

        await Screen.ImportArchivesCommand.ExecuteAsync(null);

        ImportArchivesViewModel importing = Assert.Single(_harness.Dialogs.ImportingArchives);
        Assert.EndsWith("takes about 1 request to Nexus Mods.", importing.ScanText, StringComparison.Ordinal);
        Assert.Equal(
            "Nexus Mods can't find archives by their content, so only those with Mod Organizer 2's note, or a name that gives their mod, can be identified.",
            importing.LookUpText);
        Assert.True(importing.ShowsLookUpText);
    }

    private static async Task<bool> PickStarfieldAsync(AddGameViewModel adding)
    {
        await adding.Reading;
        bool added = false;
        adding.AddRequested += (_, _) => added = true;
        adding.Picker.Query = "starf";
        GamePickerEntryViewModel entry = Assert.Single(adding.Picker.Entries);
        entry.PickCommand.Execute(null);
        return added;
    }

    private Task StartAsync()
        => _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")));
}
