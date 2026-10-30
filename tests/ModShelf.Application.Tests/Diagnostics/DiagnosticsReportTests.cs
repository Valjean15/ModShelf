using System.Runtime.InteropServices;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Diagnostics;

/// <summary>
/// The report a bug report needs: what it says, and what it never does.
/// </summary>
public sealed class DiagnosticsReportTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly InMemorySettingsStore _store = new();
    private readonly ListedLibrary _library = new();
    private readonly ListedAccounts _accounts = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly RecordingLogger<DiagnosticsReport> _logger = new();
    private readonly List<DownloadJobSnapshot> _jobs = [];
    private readonly Folders _folders = new(Path.Combine(Home, ".local", "share", "ModShelf"), Path.Combine(Home, ".local", "share", "ModShelf", "logs"));

    [Fact]
    public async Task StartsWithTheVersionTheTimeAndTheSystem()
    {
        string[] lines = await LinesAsync();

        Assert.Equal("ModShelf 0.9.0 (a60d76b)", lines[0]);
        Assert.Equal("Written: 2026-10-21 12:00 UTC", lines[1]);
        Assert.Equal($"System: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}, {RuntimeInformation.FrameworkDescription}", lines[2]);
    }

    [Fact]
    public async Task ShowsTheFoldersWithTheHomeFolderAsATilde()
    {
        _store.Settings = new AppSettings { LibraryRoot = Path.Combine(Home, "Mods") };

        string folders = Line(await LinesAsync(), "Folders");

        string separator = Path.DirectorySeparatorChar.ToString();
        Assert.Equal(
            $"Folders: data ~{separator}.local{separator}share{separator}ModShelf, logs ~{separator}.local{separator}share{separator}ModShelf{separator}logs, library ~{separator}Mods",
            folders);
    }

    [Fact]
    public async Task LeavesAFolderOutsideTheHomeFolderAsItIs()
    {
        // At the root of the home folder's drive, never inside it; the temporary folder is inside it on Windows.
        string elsewhere = Path.Combine(Path.GetPathRoot(Home)!, "ModShelf.Library");
        _store.Settings = new AppSettings { LibraryRoot = elsewhere };

        Assert.EndsWith($"library {elsewhere}", Line(await LinesAsync(), "Folders"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhatTheSettingsAreLeftAtTheirDefaults()
    {
        Assert.Equal(
            "Settings: check for updates every 6 hours, keep the last 2 versions, theme System, notices on, closing the window asks, folders for 0 games set apart",
            Line(await LinesAsync(), "Settings"));
    }

    [Fact]
    public async Task SaysWhatTheSettingsAreSetTo()
    {
        _store.Settings = new AppSettings
        {
            UpdateInterval = TimeSpan.FromMinutes(30),
            OldVersions = OldVersionRule.Archive,
            Theme = AppTheme.Dark,
            NotifyOfUpdates = false,
            CloseToTray = true,
            GameFolders = new Dictionary<string, FolderPattern> { ["skyrimspecialedition"] = FolderPattern.Default },
        };

        Assert.Equal(
            "Settings: check for updates every 30 minutes, archive old versions, theme Dark, notices off, closing the window keeps running in the tray, folders for 1 game set apart",
            Line(await LinesAsync(), "Settings"));
    }

    [Theory]
    [InlineData(24 * 60, "1 day")]
    [InlineData(7 * 24 * 60, "7 days")]
    [InlineData(60, "1 hour")]
    [InlineData(1, "1 minute")]
    [InlineData(90, "90 minutes")]
    public async Task CountsIntervalsInTheLargestWholeUnit(int minutes, string said)
    {
        _store.Settings = new AppSettings { UpdateInterval = TimeSpan.FromMinutes(minutes), OldVersions = OldVersionRule.KeepAll, CloseToTray = false };

        string settings = Line(await LinesAsync(), "Settings");

        Assert.StartsWith($"Settings: check for updates every {said}, keep every version,", settings, StringComparison.Ordinal);
        Assert.Contains("closing the window quits", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsTheLibraryAndSaysWhenItsWatchedModsWereChecked()
    {
        ModRef skyUi = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "12604");
        ModRef smapi = new(new GameRef(TestData.Nexus, "stardewvalley"), "2400");
        _library.Mods.Add(LibraryMod.Watch(skyUi, "SkyUI", null, null, TestData.Now.AddDays(-9))
            .WithFile(File(skyUi, "1"))
            .WithFile(File(skyUi, "2"))
            .Checked(TestData.Now.AddDays(-2))
            .WithUpdates([Update(skyUi, "3")]));
        _library.Mods.Add(LibraryMod.Watch(smapi, "SMAPI", null, null, TestData.Now.AddDays(-9)).Checked(TestData.Now.AddHours(-1)));
        _library.Mods.Add(LibraryMod.Watch(new ModRef(smapi.Game, "1915"), "Content Patcher", null, null, TestData.Now.AddDays(-9)).WithFile(File(new ModRef(smapi.Game, "1915"), "9")).Unwatched()!);

        string[] lines = await LinesAsync();

        Assert.Equal(
            "Library: 3 mods, 2 watched, 3 files, 1 update; every watched mod checked on 2026-10-19 12:00 UTC or later",
            Line(lines, "Library"));
        Assert.DoesNotContain(lines, line => line.Contains("SkyUI", StringComparison.Ordinal) || line.Contains("SMAPI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepingOneVersionIsOneVersion()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.KeepLatest(1) };

        Assert.Contains(", keep the last 1 version,", Line(await LinesAsync(), "Settings"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyLibraryHasNoCheck()
    {
        Assert.Equal("Library: 0 mods, 0 watched, 0 files, 0 updates; no watched mod checked yet", Line(await LinesAsync(), "Library"));
    }

    [Fact]
    public async Task CountsTheDownloadsByWhatTheyreDoing()
    {
        ModFileRef file = TestData.File(TestData.Nexus, "1");
        _jobs.AddRange(
        [
            RecordingDownloadService.Job(file, Path.GetTempPath(), "a.7z", DownloadJobStatus.Completed),
            RecordingDownloadService.Job(file, Path.GetTempPath(), "b.7z", DownloadJobStatus.Queued),
            RecordingDownloadService.Job(file, Path.GetTempPath(), "c.7z", DownloadJobStatus.Completed),
            RecordingDownloadService.Job(file, Path.GetTempPath(), "d.7z", DownloadJobStatus.AwaitingHandoff),
        ]);

        string downloads = Line(await LinesAsync(), "Downloads");

        Assert.Equal("Downloads: 1 Queued, 1 AwaitingHandoff, 2 Completed", downloads);
        Assert.DoesNotContain(".7z", downloads, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoDownloadsAreNone()
    {
        Assert.Equal("Downloads: none", Line(await LinesAsync(), "Downloads"));
    }

    [Fact]
    public async Task SaysHowEachAccountIsLinkedButNeverItsName()
    {
        _accounts.Statuses.Add(Site("nexus", new LinkedAccount(new SourceId("nexus"), AuthKind.ApiKey, new AccountProfile("Jean Lanza", tier: "Premium"), TestData.Now)));
        _accounts.Statuses.Add(Site("modio", new LinkedAccount(new SourceId("modio"), AuthKind.OAuthPkce, new AccountProfile("jeanl"), TestData.Now)));
        _accounts.Statuses.Add(Site("other", null));

        string report = string.Join('\n', await LinesAsync());

        Assert.Contains(
            "Accounts: nexus linked (ApiKey, Premium); modio linked (OAuthPkce); other not linked; secrets kept by the system",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Jean", report, StringComparison.Ordinal);
        Assert.DoesNotContain("jeanl", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenNoKeyringKeepsTheSecrets()
    {
        _secrets.IsProtectedByOs = false;

        Assert.EndsWith("secrets in files encrypted for this computer, no keyring", Line(await LinesAsync(), "Accounts"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartThatCantBeReadSaysSoWithoutItsMessageAndTheRestIsWritten()
    {
        _library.Failure = new IOException($"The database at {Home} can't be read.");

        string[] lines = await LinesAsync();

        Assert.Equal("Library: couldn't be read (IOException)", Line(lines, "Library"));
        Assert.Equal("Downloads: none", Line(lines, "Downloads"));
        Assert.StartsWith("Accounts:", lines[^1], StringComparison.Ordinal);
        Assert.StartsWith("Warning: The diagnostics report couldn't read its Library", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndsEachLineWithANewLine()
    {
        string report = await Report().DescribeAsync(TestContext.Current.CancellationToken);

        Assert.EndsWith("\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", report, StringComparison.Ordinal);
        Assert.Equal(8, report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static string Line(string[] lines, string part) => Assert.Single(lines, line => line.StartsWith($"{part}:", StringComparison.Ordinal));

    private static SourceAccountStatus Site(string id, LinkedAccount? account)
        => new(new SourceId(id), id, WorksWithoutAccount: false, [AuthKind.ApiKey], account);

    private static LibraryFile File(ModRef mod, string id)
        => new(new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, TestData.Now, $"mods/{mod.Id}/{id}.7z", 1, TestData.Now);

    private static LibraryUpdate Update(ModRef mod, string id)
        => new(new ModFileRef(mod, id), $"File {id}", $"{id}.7z", null, ModFileCategory.Main, TestData.Now, new ModFileRef(mod, "2"), TestData.Now);

    private async Task<string[]> LinesAsync()
        => (await Report().DescribeAsync(TestContext.Current.CancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private DiagnosticsReport Report()
        => new(
            new AppInfo("0.9.0", "a60d76b"),
            _folders,
            new SettingsService(_store, new FakeUserFolders(), new InMemoryLibraryRepository(), new InMemoryDownloadJobRepository([])),
            _library,
            new RecordingDownloadService([.. _jobs]),
            _accounts,
            _secrets,
            new FakeTimeProvider(TestData.Now),
            _logger);

    private sealed record Folders(string DataFolder, string LogFolder) : IAppFolders;
}
