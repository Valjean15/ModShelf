using ModShelf.Application.Releases;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// ModShelf's own updates in the About part: looking, downloading when asked, and restarting into the download when
/// asked.
/// </summary>
public sealed class AppUpdateViewModelTests
{
    private static readonly AppRelease Newer = new("0.9.1", "Fixed the update check.");

    private readonly ScriptedAppReleases _releases = new();
    private readonly RecordingAppExit _exit = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly RecordingLogger<AppUpdateViewModel> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABuildFromSourceSaysUpdatesComeWithTheInstalledApp()
    {
        AppUpdateViewModel update = Create(new ScriptedAppReleases(installed: false) { Downloaded = Newer });

        await update.CheckAtStartAsync(Cancellation);

        Assert.False(update.IsInstalled);
        Assert.Equal(Strings.AppUpdateNotInstalled, update.Status);
        Assert.Null(update.Ready);
        Assert.False(update.CheckCommand.CanExecute(null));
        Assert.False(update.WantsUser);
        Assert.Empty(_releases.Calls);
    }

    [Fact]
    public void SaysNothingBeforeItLooks()
    {
        AppUpdateViewModel update = Create();

        Assert.True(update.IsInstalled);
        Assert.Equal(string.Empty, update.Status);
        Assert.True(update.CheckCommand.CanExecute(null));
        Assert.False(update.DownloadCommand.CanExecute(null));
        Assert.False(update.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheLookAtStartOnlySaysANewerOneIsAvailable()
    {
        _releases.Newer = Newer;
        AppUpdateViewModel update = Create();

        await update.CheckAtStartAsync(Cancellation);

        Assert.Equal(Newer, update.Found);
        Assert.Equal("ModShelf 0.9.1 is available.", update.Status);
        Assert.Equal("Fixed the update check.", update.Notes);
        Assert.True(update.WantsUser);
        Assert.True(update.DownloadCommand.CanExecute(null));
        Assert.Equal(["Check"], _releases.Calls);
    }

    [Fact]
    public async Task TheLookAtStartFailsQuietly()
    {
        _releases.CheckFailure = new HttpRequestException("No such host is known.");
        AppUpdateViewModel update = Create();

        await update.CheckAtStartAsync(Cancellation);

        Assert.Equal(string.Empty, update.Status);
        Assert.Equal(string.Empty, update.Problem);
        Assert.False(update.IsChecking);
        Assert.StartsWith("Information: ModShelf couldn't look for a newer version as it started", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseDownloadedBeforeWaitsForARestartWithoutLooking()
    {
        _releases.Downloaded = Newer;
        AppUpdateViewModel update = Create();

        await update.CheckAtStartAsync(Cancellation);

        Assert.Equal(Newer, update.Ready);
        Assert.StartsWith("ModShelf 0.9.1 is downloaded. Restart ModShelf", update.Status, StringComparison.Ordinal);
        Assert.True(update.WantsUser);
        Assert.True(update.RestartCommand.CanExecute(null));
        Assert.False(update.CheckCommand.CanExecute(null));
        Assert.Empty(_releases.Calls);
    }

    [Fact]
    public async Task LookingSaysWhenThisIsTheNewest()
    {
        AppUpdateViewModel update = Create();

        await update.CheckCommand.ExecuteAsync(null);

        Assert.True(update.IsNewest);
        Assert.Equal(Strings.AppUpdateNewest, update.Status);
        Assert.False(update.WantsUser);
        Assert.Null(update.Notes);
    }

    [Fact]
    public async Task ALookThatFailsSaysWhyAndIsLogged()
    {
        _releases.CheckFailure = new IOException("ModShelf's releases couldn't be read: the feed is damaged.");
        AppUpdateViewModel update = Create();

        await update.CheckCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't look for a newer ModShelf: ModShelf's releases couldn't be read: the feed is damaged.", update.Problem);
        Assert.StartsWith("Warning: Looking for a newer ModShelf failed", Assert.Single(_logger.Entries), StringComparison.Ordinal);

        // Looking again clears it.
        await update.CheckCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, update.Problem);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsSomethingThatWentWrong()
    {
        _releases.CheckFailure = new FormatException("Unexpected.");
        AppUpdateViewModel update = Create();

        await update.CheckCommand.ExecuteAsync(null);

        Assert.Equal(Strings.SomethingWentWrong, update.Problem);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public async Task DownloadsWhenAskedShowingItsProgressThenWaitsForARestart()
    {
        _releases.Newer = Newer;
        _releases.Hold = new TaskCompletionSource();
        AppUpdateViewModel update = Create();
        await update.CheckCommand.ExecuteAsync(null);

        Task downloading = update.DownloadCommand.ExecuteAsync(null);

        Assert.True(update.IsDownloading);
        Assert.Equal(100, update.Progress);
        Assert.Equal("Downloading ModShelf 0.9.1…", update.Status);
        Assert.False(update.DownloadCommand.CanExecute(null));
        Assert.False(update.CheckCommand.CanExecute(null));
        _releases.Hold.SetResult();
        await downloading;

        Assert.False(update.IsDownloading);
        Assert.Null(update.Found);
        Assert.Equal(Newer, update.Ready);
        Assert.Equal("Fixed the update check.", update.Notes);
        Assert.True(update.WantsUser);
        Assert.True(update.RestartCommand.CanExecute(null));
        Assert.Equal(["Check", "Download 0.9.1"], _releases.Calls);
        Assert.Equal(0, _exit.Quits);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: ModShelf 0.9.1 is downloaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProgressIsShownOnTheUiThread()
    {
        _releases.Newer = Newer;
        _releases.Steps = [40];
        _uiThread.Inline = false;
        AppUpdateViewModel update = Create();
        await update.CheckCommand.ExecuteAsync(null);

        await update.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(0, update.Progress);
        _uiThread.RunPending();
        Assert.Equal(40, update.Progress);
    }

    [Fact]
    public async Task ADownloadThatFailsSaysWhyAndCanBeTriedAgain()
    {
        _releases.Newer = Newer;
        _releases.DownloadFailure = new IOException("The package's checksum doesn't match.");
        AppUpdateViewModel update = Create();
        await update.CheckCommand.ExecuteAsync(null);

        await update.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("ModShelf 0.9.1 couldn't be downloaded: The package's checksum doesn't match.", update.Problem);
        Assert.Equal(Newer, update.Found);
        Assert.Null(update.Ready);
        Assert.False(update.IsDownloading);
        Assert.True(update.DownloadCommand.CanExecute(null));
        Assert.StartsWith("Warning: Downloading ModShelf 0.9.1 failed", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartingStartsTheUpdateThenQuits()
    {
        _releases.Downloaded = Newer;
        AppUpdateViewModel update = Create();

        update.RestartCommand.Execute(null);

        Assert.Equal(["Restart 0.9.1"], _releases.Calls);
        Assert.Equal(1, _exit.Quits);
        Assert.StartsWith("Information: Restarting to update to ModShelf 0.9.1", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), "ModShelf couldn't restart to update: Refused.")]
    [InlineData(typeof(FileNotFoundException), "ModShelf couldn't restart to update: Refused.")]
    [InlineData(typeof(FormatException), null)]
    public void ARestartThatCantStartStaysAndSaysWhy(Type failure, string? problem)
    {
        _releases.Downloaded = Newer;
        _releases.RestartFailure = (Exception)Activator.CreateInstance(failure, "Refused.")!;
        AppUpdateViewModel update = Create();

        update.RestartCommand.Execute(null);

        Assert.Equal(0, _exit.Quits);
        Assert.Equal(problem ?? Strings.SomethingWentWrong, update.Problem);
        Assert.StartsWith("Error: Restarting to update to ModShelf 0.9.1 failed", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALookWhileOneRunsIsntStarted()
    {
        _releases.Newer = Newer;
        AppUpdateViewModel update = Create();
        await update.CheckCommand.ExecuteAsync(null);
        await update.DownloadCommand.ExecuteAsync(null);

        // Downloaded: there's nothing to look for until the restart.
        await update.CheckAtStartAsync(Cancellation);

        Assert.Equal(["Check", "Download 0.9.1"], _releases.Calls);
    }

    [Fact]
    public async Task ShowsTheReleaseNotesAsText()
    {
        _releases.Newer = new AppRelease("0.9.1", "## 0.9.1\r\n\n- Fixed **the** update check.\n  * Kept `settings.json`.\n\nThanks!  ");
        AppUpdateViewModel update = Create();

        await update.CheckAtStartAsync(Cancellation);

        Assert.Equal("0.9.1\n\n• Fixed the update check.\n  • Kept settings.json.\n\nThanks!", update.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    public async Task BlankNotesAreNone(string? notes)
    {
        _releases.Newer = new AppRelease("0.9.1", notes);
        AppUpdateViewModel update = Create();

        await update.CheckAtStartAsync(Cancellation);

        Assert.Null(update.Notes);
    }

    private AppUpdateViewModel Create(ScriptedAppReleases? releases = null) => new(releases ?? _releases, _exit, _uiThread, _logger);
}
