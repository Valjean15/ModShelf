using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Selecting the mods Browse lists, and Download selected: each one's newest main file into the library.
/// </summary>
public sealed class BrowseSelectionTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task CountsTheSelectedMods()
    {
        await ListAsync(5);
        (string, bool, bool, bool?) before = (Screen.SelectionText, Screen.HasSelection, Screen.DownloadSelectedCommand.CanExecute(null), Screen.AreAllShownSelected);

        Row("Mod 1").IsSelected = true;
        Row("Mod 3").IsSelected = true;
        (string, bool, string, bool, bool?) some = (Screen.SelectionText, Screen.HasSelection, Screen.DownloadSelectedText, Screen.DownloadSelectedCommand.CanExecute(null), Screen.AreAllShownSelected);
        Screen.AreAllShownSelected = true;
        string all = Screen.SelectionText;
        Screen.ClearSelectionCommand.Execute(null);

        Assert.Equal(("0 selected", false, false, false), before);
        Assert.Equal(("2 selected", true, "Download selected (2)", true, null), some);
        Assert.Equal("5 selected", all);
        Assert.Equal(("0 selected", false, false), (Screen.SelectionText, Screen.HasSelection, Screen.AreAllShownSelected));
    }

    [Fact]
    public async Task AModItsSiteNoLongerListsCantBeSelected()
    {
        List<ModInfo> mods = _harness.Searchable(BrowseHarness.Skyrim, 3);
        mods[1] = BrowseHarness.Info(mods[1].Mod, "Mod 2", isAvailable: false);
        await ChooseAsync();

        Row("Mod 2").IsSelected = true;
        bool selected = Row("Mod 2").IsSelected;
        Screen.AreAllShownSelected = true;

        Assert.False(selected);
        Assert.False(Row("Mod 2").CanSelect);
        Assert.Equal(("2 selected", true), (Screen.SelectionText, Screen.AreAllShownSelected));
    }

    [Fact]
    public async Task LoadMoreKeepsTheSelectionAndANewSearchStartsWithNone()
    {
        await ListAsync(30);
        Row("Mod 1").IsSelected = true;

        await Screen.LoadMoreCommand.ExecuteAsync(null);
        (string, bool?) afterMore = (Screen.SelectionText, Screen.AreAllShownSelected);
        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal(("1 selected", null), afterMore);
        Assert.Equal(("0 selected", false), (Screen.SelectionText, Screen.HasSelection));
    }

    [Fact]
    public async Task DownloadSelectedQueuesEachModInTheOrderListedThenUnselectsThem()
    {
        await ListAsync(5);
        Row("Mod 3").IsSelected = true;
        Row("Mod 1").IsSelected = true;
        List<(bool, bool)> during = [];
        _harness.LibraryDownloads.Answer = _ =>
        {
            during.Add((Screen.IsDownloadingSelected, Screen.DownloadSelectedCommand.CanExecute(null)));
            return Queued();
        };

        await Screen.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(
            [new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/1"), new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/3")],
            _harness.LibraryDownloads.Added);
        Assert.All(during, state => Assert.Equal((true, false), state));
        Assert.Equal(("Queued 2 files. They show on the Downloads screen.", false), (Screen.SelectionNote, Screen.IsSelectionNoteAProblem));
        Assert.Equal((false, false), (Screen.HasSelection, Screen.IsDownloadingSelected));
    }

    [Fact]
    public async Task SaysWhatWasInTheLibraryAndWhatCouldntBeQueued()
    {
        await ListAsync(3);
        Screen.AreAllShownSelected = true;
        _harness.LibraryDownloads.Refusals.Enqueue(new AuthenticationFailedException());
        _harness.LibraryDownloads.Refusals.Enqueue(new InvalidOperationException("Something broke."));
        _harness.LibraryDownloads.Answer = _ => new LibraryDownload(Path.Combine(TestData.LibraryRoot, "file.7z"), null);

        await Screen.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(
            ("1 was in the library already. 2 files couldn't be queued. The first: The site needs a linked account to look that link up, or refused the one linked. Link it in Settings › Accounts, then try again.", true),
            (Screen.SelectionNote, Screen.IsSelectionNoteAProblem));
        Assert.StartsWith(
            "Error: Queuing a file of mod 2 of skyrimspecialedition for the library failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ModsWhoseFilesAreAllInTheLibrarySaySo()
    {
        await ListAsync(2);
        Screen.AreAllShownSelected = true;
        _harness.LibraryDownloads.Answer = _ => new LibraryDownload(Path.Combine(TestData.LibraryRoot, "file.7z"), null);

        await Screen.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(("Their files are in the library already.", false), (Screen.SelectionNote, Screen.IsSelectionNoteAProblem));
    }

    [Fact]
    public async Task OneFileQueuedOrRefusedSaysSo()
    {
        await ListAsync(2);
        Row("Mod 1").IsSelected = true;
        _harness.LibraryDownloads.Answer = _ => Queued();
        await Screen.DownloadSelectedCommand.ExecuteAsync(null);
        string one = Screen.SelectionNote!;
        Row("Mod 2").IsSelected = true;
        _harness.LibraryDownloads.Refusals.Enqueue(new InvalidDataException("settings.json isn't JSON."));

        await Screen.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal("Queued 1 file. It shows on the Downloads screen.", one);
        Assert.Equal("1 file couldn't be queued: settings.json isn't JSON.", Screen.SelectionNote);
    }

    [Fact]
    public async Task AsksFirstWhenTheDownloadsLikelyUseUpTheRequests()
    {
        await ListAsync(3);
        Screen.AreAllShownSelected = true;
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromDays(1), 1000, 5, TestData.Now.AddHours(5))]);
        _harness.LibraryDownloads.Answer = _ => Queued();

        await Screen.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.StartsWith("This may take about 6 requests to Nexus Mods, and 5 are left today.", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Empty(_harness.LibraryDownloads.Added);
        Assert.Equal((true, false), (Screen.HasSelection, Screen.IsDownloadingSelected));
    }

    private BrowseModViewModel Row(string name) => Assert.Single(Screen.Results, row => row.Name == name);

    private async Task ListAsync(int mods)
    {
        _harness.Searchable(BrowseHarness.Skyrim, mods);
        await ChooseAsync();
    }

    private async Task ChooseAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAndSearchAsync(BrowseHarness.Skyrim);
    }

    private static LibraryDownload Queued()
    {
        string folder = Path.Combine(TestData.LibraryRoot, "mods");
        return new LibraryDownload(Path.Combine(folder, "file.7z"), new LinkDownload(TestData.Job("file.7z", folder: folder), AlreadyQueued: false));
    }
}
