using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Requirements;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.ModPages;

/// <summary>
/// Download with requirements, from a mod's page: SkyUI needs SKSE Address Library, which the library lacks; Mod Config
/// Menu, which it has; and a script extender off the site. Deeper, Address Library needs a mod of its own.
/// </summary>
public sealed class ModPageRequirementsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef AddressLibrary = TestData.Mod("32444");
    private static readonly ModRef ConfigMenu = TestData.Mod("1000");
    private static readonly ModRef Deeper = TestData.Mod("2000");
    private static readonly Uri ScriptExtender = new("https://skse.example/");

    private readonly BrowseHarness _harness = new();

    public ModPageRequirementsTests()
    {
        _harness.Requirements.Plans[SkyUi] =
        [
            new PlannedRequirement("Address Library", AddressLibrary, null, "For the main file", 1, SkyUi, RequirementState.ToDownload, BrowseHarness.File(AddressLibrary, "5")),
            new PlannedRequirement("Mod Config Menu", ConfigMenu, null, null, 1, SkyUi, RequirementState.InLibrary),
            new PlannedRequirement("Script Extender", null, ScriptExtender, null, 1, SkyUi, RequirementState.Elsewhere),
        ];
        _harness.Requirements.Deeper[SkyUi] =
        [
            new PlannedRequirement("Deeper Mod", Deeper, null, null, 2, AddressLibrary, RequirementState.ToDownload, BrowseHarness.File(Deeper, "7")),
            new PlannedRequirement("Gone Mod", TestData.Mod("3000"), null, null, 2, AddressLibrary, RequirementState.CantDownload, Why: "Nexus Mods lists no main file of it to download."),
        ];
        _harness.LibraryDownloads.Answer = link => new LibraryDownload(
            Path.Combine(Path.GetTempPath(), "library", link.Query),
            new LinkDownload(TestData.Job("file.7z"), AlreadyQueued: false));
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OnlyAModThatNeedsOtherModsOffersIt()
    {
        await _harness.StartAsync();

        ModPageViewModel needing = await OpenAsync(needsMods: true);
        ModPageViewModel alone = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(TestData.Mod("9"), "Alone"), BrowseHarness.File(TestData.Mod("9"), "1")));

        Assert.True(needing.NeedsMods);
        Assert.True(Assert.Single(needing.Sections).Files[0].DownloadWithRequirementsCommand.CanExecute(null));
        Assert.Equal((false, false), (alone.NeedsMods, Assert.Single(alone.Sections).Files[0].CanDownloadWithRequirements));
        Assert.False(alone.DownloadRequirementsCommand.CanExecute(null));
    }

    [Fact]
    public async Task ListsWhatItNeedsWithTheOnesToDownloadTicked()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(needsMods: true);

        DownloadRequirementsViewModel downloading = await ShowAsync(mod, fromFile: true);

        Assert.Equal(("Download SkyUI with its requirements", "With File 1, and the mods it needs that you tick."), (downloading.Title, downloading.FileText));
        Assert.Equal(
            [
                ("Address Library", "Newest main file: File 5", true, true, false),
                ("Mod Config Menu", "In library", false, false, false),
                ("Script Extender", "Off the site", false, false, true),
            ],
            downloading.Rows.Select(row => (row.Name, row.Said, row.CanChoose, row.IsChosen, row.IsElsewhere)));
        Assert.Equal("For the main file", downloading.Rows[0].Notes);
        Assert.Equal(("Download (2)", (string?)null), (downloading.DownloadText, downloading.Message));
        Assert.Equal([(SkyUi, false)], _harness.Requirements.Planned);
        Assert.Empty(_harness.LibraryDownloads.Added);
    }

    [Fact]
    public async Task DownloadsTheFileAndTheTickedOnesGoingDeeperWhenAsked()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(needsMods: true);
        DownloadRequirementsViewModel downloading = await ShowAsync(mod, fromFile: true);

        downloading.IncludesDeeper = true;
        await WaitUntilReadAsync(downloading);
        downloading.Rows.Single(row => row.Name == "Deeper Mod").IsChosen = false;
        string beforeDownloading = downloading.DownloadText;
        await downloading.DownloadCommand.ExecuteAsync(null);

        Assert.Equal([(SkyUi, false), (SkyUi, true)], _harness.Requirements.Planned);
        Assert.Equal(
            [("Deeper Mod", "Needed by Address Library"), ("Gone Mod", "Needed by Address Library")],
            downloading.Rows.Where(row => row.NeededByText.Length > 0).Select(row => (row.Name, row.NeededByText)));
        Assert.Equal("Can't download: Nexus Mods lists no main file of it to download.", downloading.Rows[^1].Said);
        Assert.Equal("Download (2)", beforeDownloading);
        Assert.Equal(
            [BrowseHarness.File(SkyUi, "1").PageUrl!, BrowseHarness.File(AddressLibrary, "5").PageUrl!],
            _harness.LibraryDownloads.Added);
        Assert.Equal((true, "Queued 2 files. They show on the Downloads screen.", "OK"), (downloading.IsDone, downloading.ResultText, downloading.CloseText));
        Assert.False(downloading.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnUntickedModStaysUntickedWhenTheListIsReadAgain()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(needsMods: true);
        DownloadRequirementsViewModel downloading = await ShowAsync(mod, fromFile: false);

        downloading.Rows[0].IsChosen = false;
        string none = downloading.DownloadText;
        downloading.IncludesDeeper = true;
        await WaitUntilReadAsync(downloading);

        Assert.Equal(("Download what SkyUI needs", (string?)null), (downloading.Title, downloading.FileText));
        Assert.Equal("Download (0)", none);
        Assert.False(downloading.Rows[0].IsChosen);
        Assert.Equal("Download (1)", downloading.DownloadText);
    }

    [Fact]
    public async Task ARequirementsListThatCantBeReadIsSaid()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(needsMods: true);
        _harness.Requirements.Failure = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        DownloadRequirementsViewModel downloading = await ShowAsync(mod, fromFile: false);

        Assert.Equal(("Nexus Mods couldn't say what it needs: Service unavailable.", true), (downloading.Message, downloading.IsMessageAProblem));
        Assert.Empty(downloading.Rows);
        Assert.False(downloading.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task ItAsksFirstWhenTheSitesRequestsLookTooFew()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(needsMods: true);
        DownloadRequirementsViewModel downloading = await ShowAsync(mod, fromFile: true);
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 500, 1, _harness.Time.GetUtcNow().AddMinutes(30))]);
        _harness.Dialogs.Confirm.Enqueue(false);

        await downloading.DownloadCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.LibraryDownloads.Added);
        Assert.False(downloading.IsDone);
    }

    /// <summary>
    /// Waits for the list read again, which ticking "their requirements too" starts without waiting.
    /// </summary>
    private static async Task WaitUntilReadAsync(DownloadRequirementsViewModel downloading)
    {
        for (int tries = 0; downloading.IsReading && tries < 100; tries++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(downloading.IsReading);
    }

    private async Task<DownloadRequirementsViewModel> ShowAsync(ModPageViewModel mod, bool fromFile)
    {
        if (fromFile)
        {
            await Assert.Single(mod.Sections).Files[0].DownloadWithRequirementsCommand.ExecuteAsync(null);
        }
        else
        {
            await mod.DownloadRequirementsCommand.ExecuteAsync(null);
        }

        return _harness.Dialogs.DownloadingRequirements[^1];
    }

    private Task<ModPageViewModel> OpenAsync(bool needsMods)
    {
        ModPage page = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"), BrowseHarness.File(SkyUi, "1"));
        return _harness.OpenAsync(needsMods
            ? page with { Requirements = [new ModRequirement("Address Library", AddressLibrary), new ModRequirement("Script Extender", pageUrl: ScriptExtender)] }
            : page);
    }
}
