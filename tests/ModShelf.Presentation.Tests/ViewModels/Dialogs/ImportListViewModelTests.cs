using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Lists;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Dialogs;

/// <summary>
/// The dialog that imports a list: what importing would do as the choices change, the import with those choices, what
/// it did and couldn't do, stopping it, and asking first when a site's requests look too few.
/// </summary>
public sealed class ImportListViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Elsewhere = new(new GameRef(new SourceId("example"), "skyrimspecialedition"), "7");

    private readonly ScriptedModLists _lists = new();
    private readonly QuotaSource _nexus = new(TestData.Nexus, "Nexus Mods");
    private readonly ScriptedDialogs _dialogs = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly RecordingLogger<ImportListViewModel> _logger = new();
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void SaysWhatImportingWouldDoAsTheChoicesChange()
    {
        ImportListViewModel importing = Dialog(Plan());
        string[] asked = Facts(importing);

        importing.IncludeOptional = true;
        string[] withOptional = Facts(importing);
        importing.WatchOnly = true;

        Assert.Equal(
            ["Mods: 3 in the list: 1 new to the library, 1 in it already, 1 of sites ModShelf can't reach here", "Files: 2 to download into the library"],
            asked);
        Assert.Equal("Files: 3 to download into the library", withOptional[1]);
        Assert.Equal("Files: None: the mods are only watched", Facts(importing)[1]);
        Assert.False(importing.DownloadFiles);
        Assert.Equal((true, "Also download the file the list marks optional"), (importing.HasOptional, importing.OptionalText));
        Assert.Equal(("Import a list", true, true, "Cancel"), (importing.Title, importing.IsAsking, importing.CanClose, importing.CloseText));
    }

    [Fact]
    public async Task ACollectionSaysWhatItNeedsFromElsewhereAndWhatItsSiteNoLongerOffers()
    {
        ListImportPlan plan = Plan();
        ImportListViewModel importing = Dialog(plan with
        {
            List = plan.List with
            {
                Elsewhere = [new ListedElsewhere("SKSE64", new Uri("https://skse.example/"), Optional: false), new ListedElsewhere("A bundled patch", null, Optional: true)],
                FilesUnavailable = 2,
            },
        });

        await importing.Elsewhere[0].OpenPageCommand.ExecuteAsync(null);

        Assert.Equal("Unavailable: 2 files the collection names are no longer offered by its site", Facts(importing)[2]);
        Assert.True(importing.HasElsewhere);
        Assert.Equal(
            [("SKSE64", false, true), ("A bundled patch", true, false)],
            importing.Elsewhere.Select(thing => (thing.Name, thing.IsOptional, thing.HasPage)));
        Assert.False(importing.Elsewhere[1].OpenPageCommand.CanExecute(null));
        Assert.Equal([new Uri("https://skse.example/")], _browser.Opened);
        Assert.False(Dialog(plan).HasElsewhere);
        Assert.Equal(2, Facts(Dialog(plan)).Length);
    }

    [Fact]
    public void TheTitleNamesAListWithATitle()
    {
        ListImportPlan plan = Plan();

        Assert.Equal("Import “Essentials”", Dialog(plan with { List = plan.List with { Title = "Essentials" } }).Title);
    }

    [Fact]
    public async Task ImportsWithTheChoicesMadeAndSaysWhatItDid()
    {
        ImportListViewModel importing = Dialog(Plan());
        importing.IncludeOptional = true;
        importing.NewestForMissing = true;
        (ImportListStage, string, bool, bool, bool)? during = null;
        ListImportResult result = new(1, [new ModFileRef(SkyUi, "1"), new ModFileRef(SkyUi, "2"), new ModFileRef(Address, "6")], 1, []);
        _lists.Importing = (_, _, _, _) =>
        {
            during = (importing.Stage, importing.ProgressText, importing.CanClose, importing.StopCommand.CanExecute(null), importing.CloseCommand.CanExecute(null));
            return Task.FromResult(result);
        };

        await importing.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new ListImportOptions(Download: true, IncludeOptional: true, NewestForMissing: true), Assert.Single(_lists.Imports).Options);
        Assert.Equal((ImportListStage.Importing, "Importing… 0 of 3 mods", false, true, false), during);
        Assert.Equal(
            "Watched 1 new mod. Queued 3 files for the library; they show on the Downloads screen. 1 file was in the library already.",
            importing.ResultText);
        Assert.Equal((true, "OK", false, false), (importing.IsDone, importing.CloseText, importing.HasProblems, importing.ImportCommand.CanExecute(null)));
        Assert.Same(result, importing.Result);
        Assert.Empty(_dialogs.Confirmations);
    }

    [Fact]
    public async Task WatchOnlyDownloadsNothing()
    {
        ImportListViewModel importing = Dialog(Plan());
        importing.IncludeOptional = true;
        importing.WatchOnly = true;

        await importing.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new ListImportOptions(Download: false, IncludeOptional: true, NewestForMissing: false), Assert.Single(_lists.Imports).Options);
        Assert.Equal("Nothing needed importing: the library has it all.", importing.ResultText);
    }

    [Fact]
    public void ImportWaitsForSomethingToImport()
    {
        ListImportPlan plan = new(
            new ModList(null, TestData.Now, [Listed(Address, "Address Library", File(Address, "5"))]),
            [new PlannedMod(Listed(Address, "Address Library", File(Address, "5")), PlannedModState.InLibrary, [new PlannedFile(File(Address, "5"), InLibrary: true)])]);
        ImportListViewModel importing = Dialog(plan);
        bool downloading = importing.ImportCommand.CanExecute(null);
        importing.WatchOnly = true;

        Assert.False(downloading);
        Assert.False(importing.ImportCommand.CanExecute(null));
        Assert.Equal("Files: None to download: the library has them all", Facts(Dialog(plan))[1]);
        Assert.True(Dialog(Plan()).ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaysWhatItCouldntDoASentenceEach()
    {
        ImportListViewModel importing = Dialog(Plan());
        _lists.Importing = (_, _, _, _) => Task.FromResult(new ListImportResult(
            0,
            [],
            0,
            [
                new ListImportProblem(Elsewhere, "Elsewhere", null, ListImportProblemKind.NoSite, null),
                new ListImportProblem(SkyUi, "SkyUI", null, ListImportProblemKind.SiteFailed, "Nexus Mods is down."),
                new ListImportProblem(Address, "Address Library", new ModFileRef(Address, "6"), ListImportProblemKind.FileGone, null),
            ]));

        await importing.ImportCommand.ExecuteAsync(null);

        Assert.True(importing.HasProblems);
        Assert.Equal(
            ["Elsewhere: ModShelf can't reach example here.", "SkyUI: its site couldn't be asked. Nexus Mods is down.", "Address Library: its site no longer offers file 6."],
            importing.Problems);
    }

    [Fact]
    public void AProblemWithoutDetailsStillSaysWhat()
    {
        Assert.Equal(
            "SkyUI: its site couldn't be asked.",
            ImportListViewModel.Said(new ListImportProblem(SkyUi, "SkyUI", null, ListImportProblemKind.SiteFailed, null)));
        Assert.Equal(
            "SkyUI: The disk is full.",
            ImportListViewModel.Said(new ListImportProblem(SkyUi, "SkyUI", new ModFileRef(SkyUi, "1"), ListImportProblemKind.NotDone, "The disk is full.")));
        Assert.Equal(
            "SkyUI: Something went wrong. The log has the details.",
            ImportListViewModel.Said(new ListImportProblem(SkyUi, "SkyUI", null, ListImportProblemKind.NotDone, null)));
    }

    [Fact]
    public async Task StopEndsTheImportAndSaysWhatStays()
    {
        ImportListViewModel importing = Dialog(Plan());
        _lists.Importing = async (_, _, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Not reached.");
        };

        Task import = importing.ImportCommand.ExecuteAsync(null);
        bool importingNow = importing.IsImporting;
        importing.StopCommand.Execute(null);
        await import;

        Assert.True(importingNow);
        Assert.Equal(("Stopped. What was queued stays on the Downloads screen.", true, true), (importing.ResultText, importing.IsDone, importing.CanClose));
        Assert.Null(importing.Result);
    }

    [Fact]
    public async Task AnImportThatFailsSaysTheLogHasWhy()
    {
        ImportListViewModel importing = Dialog(Plan());
        _lists.Importing = (_, _, _, _) => Task.FromException<ListImportResult>(new InvalidOperationException("The database is locked."));

        await importing.ImportCommand.ExecuteAsync(null);

        Assert.Equal(("The list couldn't be imported. The log says why.", true), (importing.ResultText, importing.IsDone));
        Assert.Contains(_logger.Entries, entry => entry.Contains("couldn't be imported", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsksFirstWhenASitesRequestsLookTooFew(bool goOn)
    {
        _nexus.Left(hourLeft: 3, hourLimit: 100, hourRenews: TestData.Now.AddMinutes(30), dayLeft: 900, dayLimit: 1000, dayRenews: TestData.Now.AddHours(10));
        _dialogs.Confirm.Enqueue(goOn);
        ImportListViewModel importing = Dialog(Plan());

        await importing.ImportCommand.ExecuteAsync(null);

        // Watching SkyUI, then reading the files of SkyUI and Address Library, and two requests for each of their files.
        (string Title, string Message, string _) asked = Assert.Single(_dialogs.Confirmations);
        Assert.Equal("Requests may run out", asked.Title);
        Assert.StartsWith("This may take about 7 requests to Nexus Mods, and 3 are left this hour.", asked.Message, StringComparison.Ordinal);
        Assert.Equal(goOn ? 1 : 0, _lists.Imports.Count);
        Assert.Equal(!goOn, importing.IsAsking);
    }

    private static ListImportPlan Plan()
    {
        ListedMod skyUi = Listed(SkyUi, "SkyUI", File(SkyUi, "1"), File(SkyUi, "2", optional: true));
        ListedMod address = Listed(Address, "Address Library", File(Address, "5"), File(Address, "6"));
        ListedMod elsewhere = Listed(Elsewhere, "Elsewhere", File(Elsewhere, "9"));
        return new ListImportPlan(
            new ModList(null, TestData.Now, [skyUi, address, elsewhere]),
            [
                new PlannedMod(skyUi, PlannedModState.New, [.. skyUi.Files.Select(file => new PlannedFile(file, InLibrary: false))]),
                new PlannedMod(address, PlannedModState.InLibrary, [new PlannedFile(address.Files[0], InLibrary: true), new PlannedFile(address.Files[1], InLibrary: false)]),
                new PlannedMod(elsewhere, PlannedModState.NoSite, [new PlannedFile(elsewhere.Files[0], InLibrary: false)]),
            ]);
    }

    private static ListedMod Listed(ModRef mod, string name, params ListedFile[] files)
        => new(mod, name, "Someone", null, Watched: true, Pinned: false, Note: null, files);

    private static ListedFile File(ModRef mod, string id, bool optional = false)
        => new(new ModFileRef(mod, id), $"File {id}", "1.0", optional ? ModFileCategory.Optional : ModFileCategory.Main, optional);

    private static string[] Facts(ImportListViewModel importing) => [.. importing.Facts.Select(fact => $"{fact.Label}: {fact.Value}")];

    private ImportListViewModel Dialog(ListImportPlan plan)
        => new(_lists, plan, new FakeSourceRegistry(_nexus), _dialogs, _browser, _time, _logger);
}
