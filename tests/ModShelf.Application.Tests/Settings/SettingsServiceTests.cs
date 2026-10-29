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
    public async Task SetsWhetherClosingKeepsModShelfInTheTrayAndBackToAsking()
    {
        await _settings.SetCloseToTrayAsync(false, Cancellation);
        bool? set = (await _settings.GetAsync(Cancellation)).CloseToTray;

        await _settings.SetCloseToTrayAsync(null, Cancellation);

        Assert.False(set);
        Assert.Null((await _settings.GetAsync(Cancellation)).CloseToTray);
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
        await _settings.SetLibraryFoldersAsync(" skyrimspecialedition ", flat, Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal(byId, settings.FoldersFor("stardewvalley"));
        Assert.Equal(flat, settings.FoldersFor("SkyrimSpecialEdition"));
    }

    [Fact]
    public async Task SetsPatternsBackToTheirDefaults()
    {
        await _settings.SetLibraryFoldersAsync(null, FolderPattern.Parse("{modid}/{file}"), Cancellation);
        await _settings.SetLibraryFoldersAsync("skyrimspecialedition", FolderPattern.Parse("{mod}/{file}"), Cancellation);

        await _settings.SetLibraryFoldersAsync("SKYRIMSPECIALEDITION", null, Cancellation);
        await _settings.SetLibraryFoldersAsync(null, null, Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Empty(settings.GameFolders);
        Assert.Equal(FolderPattern.Default, settings.FoldersFor("skyrimspecialedition"));
    }

    [Fact]
    public async Task AGamesPatternReplacesItsLastOneWhateverItsCase()
    {
        await _settings.SetLibraryFoldersAsync("skyrimspecialedition", FolderPattern.Parse("{mod}/{file}"), Cancellation);

        await _settings.SetLibraryFoldersAsync("SkyrimSpecialEdition", FolderPattern.Parse("{modid}/{file}"), Cancellation);

        AppSettings settings = await _settings.GetAsync(Cancellation);
        Assert.Equal("{modid}/{file}", Assert.Single(settings.GameFolders).Value.Text);
    }

    [Fact]
    public async Task RefusesABlankGame()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetLibraryFoldersAsync(" ", FolderPattern.Default, Cancellation));
    }

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
