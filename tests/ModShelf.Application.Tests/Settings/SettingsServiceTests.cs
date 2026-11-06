using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private static readonly string Mods = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Mods");

    private readonly InMemorySettingsStore _store = new();
    private readonly FakeUserFolders _folders = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemoryDownloadJobRepository _jobs = new([]);
    private readonly SettingsService _settings;

    public SettingsServiceTests()
    {
        _settings = new SettingsService(_store, _folders, _library, _jobs);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task TheLibraryIsInDownloadsUnlessSetElsewhere()
    {
        string downloads = Path.Combine(_folders.Downloads, "ModShelf");

        Assert.Equal(downloads, _settings.DefaultLibraryRoot);
        Assert.Equal(downloads, await _settings.GetLibraryRootAsync(Cancellation));
        Assert.Equal("memory", _settings.Location);
    }

    [Fact]
    public async Task SetsTheRootAsAFullPathWithoutAnEndingSeparator()
    {
        await _settings.SetLibraryRootAsync(Path.Combine(Mods, "..", "Mods") + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
        Assert.Equal(Mods, (await _settings.GetAsync(Cancellation)).LibraryRoot);
    }

    [Theory]
    [InlineData("Mods")]
    [InlineData("../Mods")]
    [InlineData(" ")]
    public async Task RefusesARootThatIsntAFullPath(string root)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetLibraryRootAsync(root, Cancellation));

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task SetsTheRootBackToTheDefault()
    {
        await _settings.SetLibraryRootAsync(Mods, Cancellation);

        await _settings.SetLibraryRootAsync(null, Cancellation);

        Assert.Null((await _settings.GetAsync(Cancellation)).LibraryRoot);
        Assert.Equal(_settings.DefaultLibraryRoot, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task KeepsTheRootWhileTheLibraryHasFiles()
    {
        _library.Add(ModWithFile());

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetLibraryRootAsync(Mods, Cancellation));

        Assert.Contains($"The library has files in {_settings.DefaultLibraryRoot}.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task SettingTheSameRootIsFineWhateverTheLibraryHolds()
    {
        await _settings.SetLibraryRootAsync(Mods, Cancellation);
        _library.Add(ModWithFile());

        await _settings.SetLibraryRootAsync(Mods, Cancellation);
        await _settings.SetLibraryRootAsync(Mods + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task OnlyWatchedModsDontKeepTheRoot()
    {
        _library.Add(LibraryMod.Watch(Smapi(), "SMAPI", null, null, DateTimeOffset.UnixEpoch));

        await _settings.SetLibraryRootAsync(Mods, Cancellation);

        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task KeepsTheRootWhileDownloadsIntoItAreUnfinished()
    {
        string root = _settings.DefaultLibraryRoot;
        await _jobs.AddAsync(Job(Path.Combine(root, "nexus"), DownloadJobStatus.Queued), Cancellation);
        await _jobs.AddAsync(Job(Path.Combine(root, "nexus"), DownloadJobStatus.Paused), Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetLibraryRootAsync(Mods, Cancellation));

        Assert.Equal($"2 downloads into {root} aren't finished. Let them finish, or cancel them, first.", refused.Message);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task FinishedDownloadsAndDownloadsElsewhereDontKeepTheRoot()
    {
        DownloadJob cancelled = Job(_settings.DefaultLibraryRoot, DownloadJobStatus.Queued);
        cancelled.Cancel();
        await _jobs.AddAsync(cancelled, Cancellation);
        await _jobs.AddAsync(Job(Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Elsewhere"), DownloadJobStatus.Queued), Cancellation);

        await _settings.SetLibraryRootAsync(Mods, Cancellation);

        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task MovingTheLibrarySetsTheRootWhateverItHolds()
    {
        _library.Add(ModWithFile());

        await _settings.MoveLibraryRootAsync(Mods + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(Mods, (await _settings.GetAsync(Cancellation)).LibraryRoot);
    }

    [Fact]
    public async Task SetsTheRuleForOldVersionsAndBackToTheDefault()
    {
        await _settings.SetOldVersionsAsync(OldVersionRule.Archive, Cancellation);
        OldVersionRule? set = (await _settings.GetAsync(Cancellation)).OldVersions;

        await _settings.SetOldVersionsAsync(null, Cancellation);

        Assert.Same(OldVersionRule.Archive, set);
        Assert.Null((await _settings.GetAsync(Cancellation)).OldVersions);
    }

    [Fact]
    public async Task SetsHowOftenToCheckForUpdatesAndBackToTheDefault()
    {
        await _settings.SetUpdateIntervalAsync(TimeSpan.FromMinutes(30), Cancellation);
        TimeSpan? set = (await _settings.GetAsync(Cancellation)).UpdateInterval;

        await _settings.SetUpdateIntervalAsync(null, Cancellation);

        Assert.Equal(TimeSpan.FromMinutes(30), set);
        Assert.Null((await _settings.GetAsync(Cancellation)).UpdateInterval);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(10081)]
    public async Task RefusesAnIntervalOutOfRange(int minutes)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetUpdateIntervalAsync(TimeSpan.FromMinutes(minutes), Cancellation));

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task SetsWhetherToNotifyOfUpdatesAndBackToTheDefault()
    {
        await _settings.SetNotifyOfUpdatesAsync(false, Cancellation);
        bool? set = (await _settings.GetAsync(Cancellation)).NotifyOfUpdates;

        await _settings.SetNotifyOfUpdatesAsync(null, Cancellation);

        Assert.False(set);
        Assert.Null((await _settings.GetAsync(Cancellation)).NotifyOfUpdates);
    }

    [Fact]
    public async Task SetsTheThemeAndBackToTheDefault()
    {
        await _settings.SetNotifyOfUpdatesAsync(false, Cancellation);
        await _settings.SetThemeAsync(AppTheme.Dark, Cancellation);
        AppSettings set = await _settings.GetAsync(Cancellation);

        await _settings.SetThemeAsync(null, Cancellation);

        Assert.Equal((AppTheme.Dark, false), (set.Theme, set.NotifyOfUpdates));
        Assert.Null((await _settings.GetAsync(Cancellation)).Theme);
        Assert.False((await _settings.GetAsync(Cancellation)).NotifyOfUpdates);
    }

    [Fact]
    public async Task SetsWhetherToCheckForANewerModShelfAndBackToYes()
    {
        await _settings.SetCheckForAppUpdatesAsync(false, Cancellation);
        bool? set = (await _settings.GetAsync(Cancellation)).CheckForAppUpdates;

        await _settings.SetCheckForAppUpdatesAsync(null, Cancellation);

        Assert.False(set);
        Assert.Null((await _settings.GetAsync(Cancellation)).CheckForAppUpdates);
    }

    [Fact]
    public async Task SetsWhetherTheFirstRunSetupIsDoneAndBackToNotYet()
    {
        Assert.Null((await _settings.GetAsync(Cancellation)).SetupDone);

        await _settings.SetSetupDoneAsync(true, Cancellation);
        bool? set = (await _settings.GetAsync(Cancellation)).SetupDone;

        await _settings.SetSetupDoneAsync(null, Cancellation);

        Assert.True(set);
        Assert.Null((await _settings.GetAsync(Cancellation)).SetupDone);
    }

    [Fact]
    public async Task SetsWhetherClosingKeepsModShelfInTheTrayAndBackToAsking()
    {
        await _settings.SetCloseToTrayAsync(false, Cancellation);
        bool? set = (await _settings.GetAsync(Cancellation)).CloseToTray;

        await _settings.SetCloseToTrayAsync(null, Cancellation);

        Assert.False(set);
        Assert.Null((await _settings.GetAsync(Cancellation)).CloseToTray);
    }

    [Fact]
    public async Task EachScreenShowsItsDefaultPreviewsUntilSet()
    {
        AppSettings settings = await _settings.GetAsync(Cancellation);

        Assert.Equal(
            [PreviewLevel.Thumbnails, PreviewLevel.Off, PreviewLevel.Cards, PreviewLevel.Off],
            Enum.GetValues<PreviewScreen>().Select(settings.PreviewFor));
    }

    [Fact]
    public async Task SetsAScreensPreviewsAndBackToItsDefault()
    {
        await _settings.SetPreviewAsync(PreviewScreen.Library, PreviewLevel.Cards, Cancellation);
        await _settings.SetPreviewAsync(PreviewScreen.Downloads, PreviewLevel.Thumbnails, Cancellation);
        AppSettings set = await _settings.GetAsync(Cancellation);

        await _settings.SetPreviewAsync(PreviewScreen.Library, null, Cancellation);
        AppSettings reset = await _settings.GetAsync(Cancellation);

        Assert.Equal((PreviewLevel.Cards, PreviewLevel.Thumbnails), (set.PreviewFor(PreviewScreen.Library), set.PreviewFor(PreviewScreen.Downloads)));
        Assert.Equal((PreviewLevel.Thumbnails, PreviewLevel.Thumbnails), (reset.PreviewFor(PreviewScreen.Library), reset.PreviewFor(PreviewScreen.Downloads)));
        Assert.Equal([PreviewScreen.Downloads], reset.Previews.Keys);
    }

    [Fact]
    public async Task RefusesPreviewsThatArentOnes()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetPreviewAsync((PreviewScreen)9, PreviewLevel.Off, Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetPreviewAsync(PreviewScreen.Browse, (PreviewLevel)9, Cancellation));

        Assert.Empty((await _settings.GetAsync(Cancellation)).Previews);
    }

    [Fact]
    public async Task SetsThePicturesRoomTheDetailsAgeAndAdultPicturesAndBackToTheirDefaults()
    {
        await _settings.SetPicturesMegabytesAsync(AppSettings.MaxPicturesMegabytes, Cancellation);
        await _settings.SetDetailsMaxAgeAsync(TimeSpan.FromDays(7), Cancellation);
        await _settings.SetAdultPicturesAsync(AdultPictures.Hidden, Cancellation);
        AppSettings set = await _settings.GetAsync(Cancellation);

        await _settings.SetPicturesMegabytesAsync(null, Cancellation);
        await _settings.SetDetailsMaxAgeAsync(null, Cancellation);
        await _settings.SetAdultPicturesAsync(null, Cancellation);
        AppSettings reset = await _settings.GetAsync(Cancellation);

        Assert.Equal((AppSettings.MaxPicturesMegabytes, TimeSpan.FromDays(7), AdultPictures.Hidden), (set.PicturesMegabytes!.Value, set.DetailsMaxAge!.Value, set.AdultPictures!.Value));
        Assert.Equal(((int?)null, (TimeSpan?)null, (AdultPictures?)null), (reset.PicturesMegabytes, reset.DetailsMaxAge, reset.AdultPictures));
    }

    [Theory]
    [InlineData(AppSettings.MinPicturesMegabytes - 1)]
    [InlineData(AppSettings.MaxPicturesMegabytes + 1)]
    public async Task RefusesRoomForPicturesOutsideTheLimits(int megabytes)
    {
        await _settings.SetPicturesMegabytesAsync(AppSettings.MinPicturesMegabytes, Cancellation);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetPicturesMegabytesAsync(megabytes, Cancellation));

        Assert.Equal(AppSettings.MinPicturesMegabytes, (await _settings.GetAsync(Cancellation)).PicturesMegabytes);
    }

    [Fact]
    public async Task SetsTheQuotaWarningAndBackToItsDefault()
    {
        await _settings.SetQuotaWarnBelowAsync(0, Cancellation);
        AppSettings off = await _settings.GetAsync(Cancellation);
        await _settings.SetQuotaWarnBelowAsync(AppSettings.MaxQuotaWarnBelow, Cancellation);
        AppSettings most = await _settings.GetAsync(Cancellation);
        await _settings.SetQuotaWarnBelowAsync(null, Cancellation);

        Assert.Equal((0, 90), (off.QuotaWarnBelow!.Value, most.QuotaWarnBelow!.Value));
        Assert.Null((await _settings.GetAsync(Cancellation)).QuotaWarnBelow);
        Assert.Equal(10, AppSettings.DefaultQuotaWarnBelow);
    }

    [Fact]
    public async Task SetsAgentsAccessAndItsPortAndBackToTheirDefaults()
    {
        await _settings.SetAgentsEnabledAsync(true, Cancellation);
        await _settings.SetAgentsPortAsync(AppSettings.MinAgentsPort, Cancellation);
        AppSettings lowest = await _settings.GetAsync(Cancellation);
        await _settings.SetAgentsPortAsync(AppSettings.MaxAgentsPort, Cancellation);
        AppSettings highest = await _settings.GetAsync(Cancellation);
        await _settings.SetAgentsEnabledAsync(null, Cancellation);
        await _settings.SetAgentsPortAsync(null, Cancellation);

        Assert.Equal((true, 1024, 65535), (lowest.AgentsEnabled, lowest.AgentsPort!.Value, highest.AgentsPort!.Value));
        AppSettings reset = await _settings.GetAsync(Cancellation);
        Assert.Equal(((bool?)null, (int?)null), (reset.AgentsEnabled, reset.AgentsPort));
        Assert.Equal(47823, AppSettings.DefaultAgentsPort);
    }

    [Theory]
    [InlineData(AppSettings.MinAgentsPort - 1)]
    [InlineData(AppSettings.MaxAgentsPort + 1)]
    [InlineData(0)]
    public async Task RefusesAnAgentsPortOutsideTheLimits(int port)
    {
        await _settings.SetAgentsPortAsync(50000, Cancellation);

        ArgumentOutOfRangeException refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetAgentsPortAsync(port, Cancellation));

        Assert.StartsWith("The port can be from 1024 to 65535.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(50000, (await _settings.GetAsync(Cancellation)).AgentsPort);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AppSettings.MaxQuotaWarnBelow + 1)]
    public async Task RefusesAQuotaWarningOutsideTheLimits(int percent)
    {
        await _settings.SetQuotaWarnBelowAsync(20, Cancellation);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetQuotaWarnBelowAsync(percent, Cancellation));

        Assert.Equal(20, (await _settings.GetAsync(Cancellation)).QuotaWarnBelow);
    }

    [Fact]
    public async Task RefusesDetailsAgesOutsideTheLimitsAndAdultPicturesThatArentOnes()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetDetailsMaxAgeAsync(TimeSpan.FromMinutes(14), Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetDetailsMaxAgeAsync(TimeSpan.FromDays(8), Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetAdultPicturesAsync((AdultPictures)3, Cancellation));

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal(((TimeSpan?)null, (AdultPictures?)null), (settings.DetailsMaxAge, settings.AdultPictures));
    }

    [Fact]
    public async Task RefusesAThemeThatIsntOne()
    {
        await _settings.SetThemeAsync(AppTheme.Light, Cancellation);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _settings.SetThemeAsync((AppTheme)3, Cancellation));

        Assert.Equal(AppTheme.Light, (await _settings.GetAsync(Cancellation)).Theme);
    }

    [Fact]
    public async Task SetsThePatternForEveryGameAndForOne()
    {
        FolderPattern byId = FolderPattern.Parse("{source}/{game}/{modid}/{file}");
        FolderPattern flat = FolderPattern.Parse("{mod}/{file}");

        await _settings.SetLibraryFoldersAsync(null, byId, Cancellation);
        await _settings.SetLibraryFoldersAsync(Nexus("skyrimspecialedition"), flat, Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal(byId, settings.FoldersFor(Nexus("stardewvalley")));
        Assert.Equal(flat, settings.FoldersFor(Nexus("SkyrimSpecialEdition")));
    }

    [Fact]
    public async Task SetsPatternsBackToTheirDefaults()
    {
        await _settings.SetLibraryFoldersAsync(null, FolderPattern.Parse("{modid}/{file}"), Cancellation);
        await _settings.SetLibraryFoldersAsync(Nexus("skyrimspecialedition"), FolderPattern.Parse("{mod}/{file}"), Cancellation);

        await _settings.SetLibraryFoldersAsync(Nexus("SKYRIMSPECIALEDITION"), null, Cancellation);
        await _settings.SetLibraryFoldersAsync(null, null, Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Empty(settings.Games);
        Assert.Equal(FolderPattern.Default, settings.FoldersFor(Nexus("skyrimspecialedition")));
    }

    [Fact]
    public async Task AGamesPatternReplacesItsLastOneWhateverItsCase()
    {
        await _settings.SetLibraryFoldersAsync(Nexus("skyrimspecialedition"), FolderPattern.Parse("{mod}/{file}"), Cancellation);

        await _settings.SetLibraryFoldersAsync(Nexus("SkyrimSpecialEdition"), FolderPattern.Parse("{modid}/{file}"), Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal("{modid}/{file}", Assert.Single(settings.Games).Value.Folders?.Text);
    }

    [Fact]
    public void RefusesABlankGame()
    {
        // A game is named by its site and its key there; a blank key names none, so it never reaches the settings.
        Assert.Throws<ArgumentException>(() => Nexus(" "));
    }

    [Fact]
    public async Task ChangesAGamesOwnSettingsAndKeepsTheOthers()
    {
        await _settings.SetLibraryFoldersAsync(Nexus("stardewvalley"), FolderPattern.Parse("{mod}/{file}"), Cancellation);

        await _settings.SetGameAsync(Nexus("StardewValley"), game => game with { Hidden = true, UpdateInterval = TimeSpan.FromDays(1) }, Cancellation);
        await _settings.SetGameAsync(Nexus("STARDEWVALLEY"), game => game with { Notify = false }, Cancellation);

        (GameRef key, GameSettings game) = Assert.Single((await _settings.GetAsync(Cancellation)).Games);
        Assert.Equal(Nexus("stardewvalley"), key);
        Assert.Equal(
            new GameSettings { Folders = FolderPattern.Parse("{mod}/{file}"), Hidden = true, UpdateInterval = TimeSpan.FromDays(1), Notify = false },
            game);
    }

    [Fact]
    public async Task TheSameKeyOnTwoSitesIsTwoGamesWithSettingsOfTheirOwn()
    {
        GameRef other = new(new SourceId("example"), "stardewvalley");

        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { Hidden = true }, Cancellation);
        await _settings.SetGameAsync(other, game => game with { Notify = false }, Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal(2, settings.Games.Count);
        Assert.Equal(new GameSettings { Hidden = true }, settings.GameFor(Nexus("stardewvalley")));
        Assert.Equal(new GameSettings { Notify = false }, settings.GameFor(other));
    }

    [Fact]
    public async Task SettingAGamesPatternKeepsItsOtherSettings()
    {
        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { CheckUpdates = false }, Cancellation);

        await _settings.SetLibraryFoldersAsync(Nexus("stardewvalley"), FolderPattern.Parse("{mod}/{file}"), Cancellation);
        await _settings.SetLibraryFoldersAsync(Nexus("stardewvalley"), null, Cancellation);

        Assert.Equal(new GameSettings { CheckUpdates = false }, Assert.Single((await _settings.GetAsync(Cancellation)).Games).Value);
    }

    [Fact]
    public async Task AGameLeftWithNoSettingOfItsOwnIsForgotten()
    {
        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { Hidden = true, Browse = new BrowseDefaults { PageSize = 50 } }, Cancellation);

        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { Hidden = null, Browse = BrowseDefaults.None }, Cancellation);

        Assert.Empty((await _settings.GetAsync(Cancellation)).Games);
    }

    [Fact]
    public async Task BrowseDefaultsLeftAsBrowsesOwnAreLeftOut()
    {
        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { Hidden = true, Browse = BrowseDefaults.None }, Cancellation);

        Assert.Equal(new GameSettings { Hidden = true }, Assert.Single((await _settings.GetAsync(Cancellation)).Games).Value);
    }

    [Fact]
    public async Task RefusesNoGameOrNoChange()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _settings.SetGameAsync(null!, game => game, Cancellation));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _settings.SetGameAsync(Nexus("stardewvalley"), null!, Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _settings.SetGameAsync(Nexus("stardewvalley"), _ => null!, Cancellation));
    }

    [Fact]
    public async Task AGameTakesEveryOneOfAnothersSettingsInPlaceOfItsOwn()
    {
        GameRef other = new(new SourceId("example"), "stardew-valley");
        GameSettings copied = new()
        {
            Folders = FolderPattern.Parse("{mod}/{file}"),
            OldVersions = OldVersionRule.Archive,
            CheckUpdates = false,
            UpdateInterval = TimeSpan.FromDays(1),
            Notify = false,
            Browse = new BrowseDefaults { PageSize = 50 },
            Hidden = true,
        };
        await _settings.SetGameAsync(other, _ => copied, Cancellation);
        await _settings.SetGameAsync(Nexus("stardewvalley"), game => game with { OldVersions = OldVersionRule.KeepAll, Notify = true }, Cancellation);

        await _settings.CopyGameAsync(other, Nexus("stardewvalley"), Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal(copied, settings.GameFor(Nexus("stardewvalley")));
        Assert.Equal(copied, settings.GameFor(other));
    }

    [Fact]
    public async Task TakingTheSettingsOfAGameWithoutAnyForgetsTheGamesOwn()
    {
        await _settings.SetGameAsync(Nexus("STARDEWVALLEY"), game => game with { Hidden = true }, Cancellation);

        await _settings.CopyGameAsync(new GameRef(new SourceId("example"), "stardew-valley"), Nexus("stardewvalley"), Cancellation);

        Assert.Empty((await _settings.GetAsync(Cancellation)).Games);
    }

    [Fact]
    public async Task AGameCantTakeItsOwnSettings()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.CopyGameAsync(Nexus("stardewvalley"), Nexus("StardewValley"), Cancellation));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _settings.CopyGameAsync(null!, Nexus("stardewvalley"), Cancellation));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _settings.CopyGameAsync(Nexus("stardewvalley"), null!, Cancellation));
    }

    private static GameRef Nexus(string key) => new(new SourceId("nexus"), key);

    private static ModRef Smapi() => new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    private static DownloadJob Job(string folder, DownloadJobStatus status)
    {
        DownloadJob job = DownloadJob.Create(new ModFileRef(Smapi(), "160380"), folder, "SMAPI.zip", DateTimeOffset.UnixEpoch);
        if (status == DownloadJobStatus.Paused)
        {
            job.Pause();
        }

        return job;
    }

    private static LibraryMod ModWithFile()
        => LibraryMod.Watch(Smapi(), "SMAPI", null, null, DateTimeOffset.UnixEpoch).WithFile(new LibraryFile(
            new ModFileRef(Smapi(), "160380"), "SMAPI 4.5.2", "4.5.2", ModFileCategory.Main, DateTimeOffset.UnixEpoch, "nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip", 1, DateTimeOffset.UnixEpoch));
}
