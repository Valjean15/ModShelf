using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Update all on a game's heading in the Library: its mods' updates downloaded into the library, and what it says.
/// </summary>
public sealed class LibraryUpdateAllTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task CountsTheUpdatesItCanDownload()
    {
        await _harness.StartAsync(
            WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true), Update(SkyUi, "3", linked: false)),
            WithUpdates(Address, "Address Library", Update(Address, "2", linked: true)),
            WithUpdates(Smapi, "SMAPI", Update(Smapi, "2", linked: false)));

        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        LibraryGameViewModel stardew = _harness.Game(Smapi.Game);

        Assert.Equal(("Update all (2)", true), (skyrim.UpdateAllText, skyrim.UpdateAllCommand.CanExecute(null)));
        Assert.Equal(("Update all (0)", false), (stardew.UpdateAllText, stardew.UpdateAllCommand.CanExecute(null)));
    }

    [Fact]
    public async Task DownloadsTheGamesUpdatesModByModAndSaysSo()
    {
        await _harness.StartAsync(
            WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true, daysAgo: 2), Update(SkyUi, "3", linked: true, daysAgo: 1)),
            WithUpdates(Address, "Address Library", Update(Address, "2", linked: true)),
            WithUpdates(Smapi, "SMAPI", Update(Smapi, "2", linked: true)));
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        List<(bool, bool)> during = [];
        _harness.LibraryDownloads.Answer = _ =>
        {
            during.Add((skyrim.IsUpdating, skyrim.UpdateAllCommand.CanExecute(null)));
            return Queued();
        };

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(["3863/2", "12604/3", "12604/2"], _harness.LibraryDownloads.Added.Select(Of));
        Assert.All(during, state => Assert.Equal((true, false), state));
        Assert.Equal(("Queued 3 updates. They show on the Downloads screen.", false), (skyrim.UpdateNote, skyrim.IsUpdateNoteAProblem));
        Assert.Equal((false, true), (skyrim.IsUpdating, skyrim.UpdateAllCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AChangeWhileUpdatingDoesntLetUpdateAllRunAgain()
    {
        LibraryMod skyUi = WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true), Update(SkyUi, "3", linked: true));
        await _harness.StartAsync(skyUi);
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        List<bool> during = [];
        _harness.LibraryDownloads.Answer = _ =>
        {
            _harness.Library.Change(SkyUi, skyUi.WithUpdates([.. skyUi.Updates]));
            during.Add(skyrim.UpdateAllCommand.CanExecute(null));
            return Queued();
        };

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal([false, false], during);
        Assert.True(skyrim.UpdateAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task OneQueuedUpdateSaysSo()
    {
        await _harness.StartAsync(WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true)));
        _harness.LibraryDownloads.Answer = _ => Queued();
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal("Queued 1 update. It shows on the Downloads screen.", skyrim.UpdateNote);
    }

    [Fact]
    public async Task UpdatesThatCouldntBeQueuedAreSaidAndTheOthersGoOn()
    {
        await _harness.StartAsync(
            WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true)),
            WithUpdates(Address, "Address Library", Update(Address, "2", linked: true)),
            WithUpdates(TestData.Mod("1"), "Other", Update(TestData.Mod("1"), "2", linked: true)));
        _harness.LibraryDownloads.Refusals.Enqueue(new AuthenticationFailedException());
        _harness.LibraryDownloads.Refusals.Enqueue(new InvalidOperationException("Something broke."));
        _harness.LibraryDownloads.Answer = _ => Queued();
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(3, _harness.LibraryDownloads.Added.Count);
        Assert.Equal(
            ("Queued 1 update. It shows on the Downloads screen. 2 updates couldn't be queued. The first: The site needs a linked account to look that link up, or refused the one linked. Link it in Settings › Accounts, then try again.", true),
            (skyrim.UpdateNote, skyrim.IsUpdateNoteAProblem));
        Assert.StartsWith(
            "Error: Queuing a file of mod 1 of skyrimspecialedition for the library failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task OneUpdateThatCouldntBeQueuedSaysWhy()
    {
        await _harness.StartAsync(WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true)));
        _harness.LibraryDownloads.Refusals.Enqueue(new InvalidDataException("settings.json isn't JSON."));
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(("1 update couldn't be queued: settings.json isn't JSON.", true), (skyrim.UpdateNote, skyrim.IsUpdateNoteAProblem));
    }

    [Fact]
    public async Task UpdatesInTheLibraryAlreadySaySo()
    {
        await _harness.StartAsync(WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true)));
        _harness.LibraryDownloads.Answer = _ => new LibraryDownload(Path.Combine(TestData.LibraryRoot, "file.7z"), null);
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(("The updates' files are in the library already.", false), (skyrim.UpdateNote, skyrim.IsUpdateNoteAProblem));
    }

    [Fact]
    public async Task UpdatesInTheLibraryAlreadyAreCountedBesideThoseQueued()
    {
        await _harness.StartAsync(
            WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true)),
            WithUpdates(Address, "Address Library", Update(Address, "2", linked: true)));
        int asked = 0;
        _harness.LibraryDownloads.Answer = _ => ++asked == 1
            ? new LibraryDownload(Path.Combine(TestData.LibraryRoot, "file.7z"), null)
            : Queued();
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(
            ("Queued 1 update. It shows on the Downloads screen. 1 was in the library already.", false),
            (skyrim.UpdateNote, skyrim.IsUpdateNoteAProblem));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsksFirstWhenTheUpdatesLikelyUseUpTheRequests(bool goOn)
    {
        await _harness.StartAsync(WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", linked: true, daysAgo: 2), Update(SkyUi, "3", linked: true, daysAgo: 1)));
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 100, 3, TestData.Now.AddMinutes(30))]);
        _harness.Dialogs.Confirm.Enqueue(goOn);
        _harness.LibraryDownloads.Answer = _ => Queued();

        await _harness.Game(SkyUi.Game).UpdateAllCommand.ExecuteAsync(null);

        Assert.StartsWith("This may take about 4 requests to Nexus Mods, and 3 are left this hour.", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Equal(goOn ? 2 : 0, _harness.LibraryDownloads.Added.Count);
    }

    private static string Of(Uri link) => $"{link.Segments[^1]}{link.Query.Replace("?tab=files&file_id=", "/", StringComparison.Ordinal)}";

    private static LibraryMod WithUpdates(ModRef mod, string name, params LibraryUpdate[] updates)
        => TestData.LibraryMod(mod, name, watched: true, TestData.LibraryFile(mod, "1", $"{name}.7z")).WithUpdates(updates);

    private static LibraryUpdate Update(ModRef mod, string id, bool linked, int daysAgo = 0)
        => new(
            new ModFileRef(mod, id),
            $"Update {id}",
            $"update-{id}.7z",
            id,
            ModFileCategory.Main,
            TestData.Now.AddDays(-daysAgo),
            new ModFileRef(mod, "1"),
            TestData.Now,
            linked ? new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}?tab=files&file_id={id}") : null);

    private static LibraryDownload Queued()
    {
        string folder = Path.Combine(TestData.LibraryRoot, "mods");
        return new LibraryDownload(Path.Combine(folder, "file.7z"), new LinkDownload(TestData.Job("file.7z", folder: folder), AlreadyQueued: false));
    }
}
