using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Releases;
using ModShelf.Application.Releases.Ports;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// ModShelf's own updates, in the About part of the Settings screen: looking for a newer release, at start or when asked;
/// downloading it when the user says; and restarting into it when the user says. A ModShelf that wasn't installed can't
/// update itself, and says so.
/// </summary>
public sealed partial class AppUpdateViewModel : ObservableObject
{
    private readonly IAppReleases _releases;
    private readonly IAppExit _exit;
    private readonly IUiThread _uiThread;
    private readonly ILogger<AppUpdateViewModel> _logger;

    public AppUpdateViewModel(IAppReleases releases, IAppExit exit, IUiThread uiThread, ILogger<AppUpdateViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _releases = releases;
        _exit = exit;
        _uiThread = uiThread;
        _logger = logger;
        IsInstalled = releases.IsInstalled;

        // One downloaded before, and not restarted into: it waits for the user still.
        Ready = IsInstalled ? releases.Downloaded : null;
    }

    /// <summary>
    /// Whether this ModShelf was installed, and so can update itself.
    /// </summary>
    public bool IsInstalled { get; }

    /// <summary>
    /// A newer release, found but not downloaded; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Notes), nameof(WantsUser))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial AppRelease? Found { get; private set; }

    /// <summary>
    /// A newer release, downloaded and waiting for a restart; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Notes), nameof(WantsUser))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(RestartCommand))]
    public partial AppRelease? Ready { get; private set; }

    /// <summary>
    /// Whether a look for a newer release is under way.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial bool IsChecking { get; private set; }

    /// <summary>
    /// Whether the last look found nothing newer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial bool IsNewest { get; private set; }

    /// <summary>
    /// Whether <see cref="Found"/> is downloading.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand))]
    public partial bool IsDownloading { get; private set; }

    /// <summary>
    /// How much of <see cref="Found"/> is downloaded, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial int Progress { get; private set; }

    /// <summary>
    /// Where ModShelf's updates stand, such as "ModShelf 0.9.1 is available."; empty before a first look.
    /// </summary>
    public string Status
        => !IsInstalled ? Strings.AppUpdateNotInstalled
            : Ready is { } ready ? Text.Format(Strings.AppUpdateReady, ready.Version)
            : IsDownloading && Found is { } downloading ? Text.Format(Strings.AppUpdateDownloading, downloading.Version)
            : Found is { } found ? Text.Format(Strings.AppUpdateAvailable, found.Version)
            : IsChecking ? Strings.AppUpdateChecking
            : IsNewest ? Strings.AppUpdateNewest
            : string.Empty;

    /// <summary>
    /// What's new in the release found or downloaded, as its release notes say, as plain text; null for none.
    /// </summary>
    public string? Notes => PlainText((Ready ?? Found)?.Notes);

    /// <summary>
    /// Whether a newer release waits for the user, to download or to restart into, which the Settings screen's badge
    /// shows.
    /// </summary>
    public bool WantsUser => Found is not null || Ready is not null;

    /// <summary>
    /// Why the last command didn't work; empty for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>
    /// The look at start: only says whether a newer release is available, and downloads nothing. A failure, such as with
    /// no connection, is only logged; looking again from the Settings screen says why.
    /// </summary>
    public async Task CheckAtStartAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled || Ready is not null || IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            Show(await _releases.CheckAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            LogStartCheckFailed(_logger, exception);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Looks for a newer release now.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync(CancellationToken cancellationToken)
        => RunAsync(
            async () =>
            {
                IsChecking = true;
                try
                {
                    Show(await _releases.CheckAsync(cancellationToken));
                }
                finally
                {
                    IsChecking = false;
                }
            },
            exception => Text.Format(Strings.AppUpdateCantCheck, exception.Message),
            exception => LogCheckFailed(_logger, exception),
            cancellationToken);

    private bool CanCheck() => IsInstalled && !IsChecking && !IsDownloading && Ready is null;

    /// <summary>
    /// Downloads the release found, for a restart into it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync(CancellationToken cancellationToken)
    {
        AppRelease release = Found!;
        return RunAsync(
            async () =>
            {
                Progress = 0;
                IsDownloading = true;
                try
                {
                    await _releases.DownloadAsync(release, new UiProgress(this), cancellationToken);
                }
                finally
                {
                    IsDownloading = false;
                }

                Found = null;
                Ready = release;
                LogDownloaded(_logger, release.Version);
            },
            exception => Text.Format(Strings.AppUpdateCantDownload, release.Version, exception.Message),
            exception => LogDownloadFailed(_logger, exception, release.Version),
            cancellationToken);
    }

    private bool CanDownload() => Found is not null && Ready is null && !IsDownloading;

    /// <summary>
    /// Quits, and starts the update, which starts the new release once ModShelf has ended.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestart))]
    private void Restart()
    {
        AppRelease release = Ready!;
        Problem = string.Empty;
        try
        {
            _releases.RestartToApply(release);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // Such as the updater that can't be started, or a download that's gone.
            LogRestartFailed(_logger, exception, release.Version);
            Problem = Text.Format(Strings.AppUpdateCantRestart, exception.Message);
            return;
        }
        catch (Exception exception)
        {
            LogRestartFailed(_logger, exception, release.Version);
            Problem = Strings.SomethingWentWrong;
            return;
        }

        LogRestarting(_logger, release.Version);
        _exit.Quit();
    }

    private bool CanRestart() => Ready is not null;

    /// <summary>
    /// Release notes, written in Markdown, as text to read: without the marks of headings, emphasis and code, and with
    /// a bullet for each item of a list.
    /// </summary>
    private static string? PlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        IEnumerable<string> lines = markdown.ReplaceLineEndings("\n").Split('\n').Select(line =>
        {
            string text = line.TrimEnd().Replace("**", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
            string content = text.TrimStart();
            string indent = text[..^content.Length];
            return content.StartsWith('#') ? content.TrimStart('#').TrimStart()
                : content.StartsWith("- ", StringComparison.Ordinal) || content.StartsWith("* ", StringComparison.Ordinal) ? $"{indent}• {content[2..]}"
                : text;
        });
        return string.Join('\n', lines).Trim();
    }

    private void Show(AppRelease? newer)
    {
        Found = newer;
        IsNewest = newer is null;
    }

    /// <summary>
    /// Runs a command; a problem with the releases, such as no connection, is shown, and anything else is logged and
    /// shown as something that went wrong. Either way, it's logged.
    /// </summary>
    private async Task RunAsync(Func<Task> work, Func<Exception, string> describe, Action<Exception> log, CancellationToken cancellationToken)
    {
        Problem = string.Empty;
        try
        {
            await work();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            log(exception);
            Problem = describe(exception);
        }
        catch (Exception exception)
        {
            log(exception);
            Problem = Strings.SomethingWentWrong;
        }
    }

    /// <summary>
    /// The download's progress, reported from wherever it runs and shown on the UI thread.
    /// </summary>
    private sealed class UiProgress(AppUpdateViewModel update) : IProgress<int>
    {
        public void Report(int value) => update._uiThread.Post(() => update.Progress = Math.Clamp(value, 0, 100));
    }
}
