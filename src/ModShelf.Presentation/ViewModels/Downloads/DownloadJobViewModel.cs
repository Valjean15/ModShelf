using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// One download on the Downloads screen: its file, where it goes, how far it is, and what can be done with it now.
/// </summary>
/// <remarks>
/// The screen gives it each new snapshot of its job, on the UI thread. What the user does goes to the queue, and shows
/// once the queue's next snapshot arrives.
/// </remarks>
public sealed partial class DownloadJobViewModel : ObservableObject
{
    // The speed is measured over the last few seconds, and only once there's a second to measure.
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortestMeasure = TimeSpan.FromSeconds(1);

    private readonly DownloadJobServices _services;
    private readonly List<(DateTimeOffset At, long Bytes)> _samples = [];
    private DownloadJobSnapshot _job;

    internal DownloadJobViewModel(DownloadJobSnapshot job, string siteName, DownloadJobServices services)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);
        ArgumentNullException.ThrowIfNull(services);

        _job = job;
        _services = services;
        SiteName = siteName;
        OpenPageLabel = Text.Format(Strings.JobOpenPage, siteName);
        Show(job);
    }

    /// <summary>
    /// What the user asked of a download, as the log names it.
    /// </summary>
    private enum JobAction
    {
        Pause,
        Resume,
        Retry,
        Cancel,
        OpenPage,
        PasteLink,
    }

    public DownloadJobId Id => _job.Id;

    public string FileName => _job.FileName;

    /// <summary>
    /// The folder the file goes into.
    /// </summary>
    public string Folder => _job.TargetDirectory;

    /// <summary>
    /// The name of the mod site the file comes from, such as Nexus Mods.
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// When the job was added, which orders the list.
    /// </summary>
    public DateTimeOffset CreatedAt => _job.CreatedAt;

    public string OpenPageLabel { get; }

    [ObservableProperty]
    public partial DownloadJobStatus Status { get; private set; }

    /// <summary>
    /// Where the download stands, in words; for a failed one, why it failed.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFailed { get; private set; }

    /// <summary>
    /// Whether the download is done or cancelled, so it can be cleared from the list.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFinished { get; private set; }

    /// <summary>
    /// Whether the download waits for the user to start it on its site, and to paste the link the site hands over.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteLinkCommand))]
    public partial bool IsAwaitingHandoff { get; private set; }

    /// <summary>
    /// How much is downloaded, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>
    /// Whether the progress can't be told, such as while the size isn't known, or the file is being checked.
    /// </summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    /// <summary>
    /// Such as "12.3 MB of 45.6 MB"; empty before anything is downloaded of a file whose size isn't known.
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "2.10 MB/s, about 3 min left", while downloading; empty until it can be measured.
    /// </summary>
    [ObservableProperty]
    public partial string SpeedText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    public partial bool CanPause { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    public partial bool CanResume { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool CanCancel { get; private set; }

    /// <summary>
    /// Whether the page where the user starts the download is known. Just after the app starts, the queue may still be
    /// asking the site for it.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPageCommand))]
    public partial bool CanOpenPage { get; private set; }

    /// <summary>
    /// Shows the job as <paramref name="job"/> has it now. Runs on the UI thread.
    /// </summary>
    internal void Show(DownloadJobSnapshot job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Id != _job.Id)
        {
            throw new ArgumentException("That's another job's snapshot.", nameof(job));
        }

        _job = job;
        DownloadJobStatus status = job.Status;
        Status = status;
        StatusText = Describe(job);
        IsFailed = status == DownloadJobStatus.Failed;
        IsFinished = status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled;
        IsAwaitingHandoff = status == DownloadJobStatus.AwaitingHandoff;
        CanPause = status is DownloadJobStatus.Queued or DownloadJobStatus.Resolving or DownloadJobStatus.AwaitingHandoff or DownloadJobStatus.Downloading;
        CanResume = status == DownloadJobStatus.Paused;
        CanRetry = status == DownloadJobStatus.Failed;
        CanCancel = !IsFinished;
        CanOpenPage = IsAwaitingHandoff && job.HandoffPage is not null;

        bool sizeKnown = job.TotalBytes is > 0;
        Progress = status == DownloadJobStatus.Completed ? 100 : sizeKnown ? Math.Min(100, 100.0 * job.BytesDownloaded / job.TotalBytes!.Value) : 0;
        IsProgressIndeterminate = status is DownloadJobStatus.Resolving or DownloadJobStatus.Verifying
            || (status == DownloadJobStatus.Downloading && !sizeKnown);
        SizeText = DescribeSize(job);
        SpeedText = Measure(job);
    }

    private static string DescribeSize(DownloadJobSnapshot job)
    {
        if (job.TotalBytes is not { } total)
        {
            return job.BytesDownloaded > 0 ? Sizes.Format(job.BytesDownloaded) : string.Empty;
        }

        return job.Status == DownloadJobStatus.Completed
            ? Sizes.Format(total)
            : Text.Format(Strings.JobSize, Sizes.Format(job.BytesDownloaded), Sizes.Format(total));
    }

    private static string DescribeFailure(DownloadFailureReason? reason) => reason switch
    {
        DownloadFailureReason.LinkUnavailable => Strings.JobFailedLinkUnavailable,
        DownloadFailureReason.HandoffExpired => Strings.JobFailedHandoffExpired,
        DownloadFailureReason.Network => Strings.JobFailedNetwork,
        DownloadFailureReason.Storage => Strings.JobFailedStorage,
        DownloadFailureReason.HashMismatch => Strings.JobFailedHashMismatch,
        DownloadFailureReason.AccountNeeded => Strings.JobFailedAccountNeeded,
        DownloadFailureReason.RateLimited => Strings.JobFailedRateLimited,
        _ => Strings.JobFailedUnexpected,
    };

    /// <summary>
    /// Such as "less than a minute left", "about 3 min left" or "about 1.5 h left".
    /// </summary>
    private static string DescribeTimeLeft(double seconds)
    {
        if (seconds < 60)
        {
            return Strings.JobLeftUnderAMinute;
        }

        double minutes = Math.Ceiling(seconds / 60);
        return minutes < 60
            ? Text.Format(Strings.JobLeftMinutes, minutes)
            : Text.Format(Strings.JobLeftHours, (seconds / 3600).ToString("0.#", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// What the queue refuses because the job changed before it got to it, such as a download that finished just before
    /// it was paused, or that was cleared: nothing to tell, since the job's next snapshot shows what it does now.
    /// </summary>
    private static async Task IgnoringRacesAsync(Func<Task> control)
    {
        try
        {
            await control();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            // Changed meanwhile.
        }
    }

    private string Describe(DownloadJobSnapshot job) => job.Status switch
    {
        DownloadJobStatus.Queued => Strings.JobQueued,
        DownloadJobStatus.Resolving => Strings.JobResolving,
        DownloadJobStatus.AwaitingHandoff => Text.Format(Strings.JobAwaitingHandoff, SiteName),
        DownloadJobStatus.Downloading => Strings.JobDownloading,
        DownloadJobStatus.Verifying => Strings.JobVerifying,
        DownloadJobStatus.Completed => Strings.JobCompleted,
        DownloadJobStatus.Paused => Strings.JobPaused,
        DownloadJobStatus.Cancelled => Strings.JobCancelled,
        _ => DescribeFailure(job.FailureReason),
    };

    /// <summary>
    /// The speed over the last few seconds, and the time left at that speed; empty when not downloading, or before
    /// there's a second to measure.
    /// </summary>
    private string Measure(DownloadJobSnapshot job)
    {
        if (job.Status != DownloadJobStatus.Downloading)
        {
            _samples.Clear();
            return string.Empty;
        }

        // A download that starts again from the beginning starts a new measure.
        DateTimeOffset now = _services.TimeProvider.GetUtcNow();
        if (_samples.Count > 0 && job.BytesDownloaded < _samples[^1].Bytes)
        {
            _samples.Clear();
        }

        _samples.Add((now, job.BytesDownloaded));
        _samples.RemoveAll(sample => now - sample.At > SpeedWindow);

        TimeSpan measured = now - _samples[0].At;
        if (measured < ShortestMeasure)
        {
            return string.Empty;
        }

        double bytesPerSecond = (job.BytesDownloaded - _samples[0].Bytes) / measured.TotalSeconds;
        string speed = Sizes.Format((long)bytesPerSecond);
        return job.TotalBytes is { } total && bytesPerSecond > 0
            ? Text.Format(Strings.JobSpeed, speed, DescribeTimeLeft(Math.Max(0, total - job.BytesDownloaded) / bytesPerSecond))
            : Text.Format(Strings.JobSpeedOnly, speed);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync(CancellationToken cancellationToken)
        => RunAsync(JobAction.Pause, () => IgnoringRacesAsync(() => _services.Downloads.PauseAsync(Id, cancellationToken)), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task ResumeAsync(CancellationToken cancellationToken)
        => RunAsync(JobAction.Resume, () => IgnoringRacesAsync(() => _services.Downloads.ResumeAsync(Id, cancellationToken)), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync(CancellationToken cancellationToken)
        => RunAsync(JobAction.Retry, () => IgnoringRacesAsync(() => _services.Downloads.RetryAsync(Id, cancellationToken)), cancellationToken);

    /// <summary>
    /// Cancels the download, which deletes what's downloaded of it; so with bytes downloaded, only once the user confirms.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private Task CancelAsync(CancellationToken cancellationToken)
        => RunAsync(
            JobAction.Cancel,
            async () =>
            {
                long downloaded = _job.BytesDownloaded;
                if (downloaded > 0)
                {
                    string message = Text.Format(Strings.CancelDownloadMessage, Sizes.Format(downloaded), FileName);
                    if (!await _services.Dialogs.ConfirmAsync(Strings.CancelDownloadTitle, message, Strings.CancelDownloadConfirm, cancellationToken))
                    {
                        return;
                    }
                }

                await IgnoringRacesAsync(() => _services.Downloads.CancelAsync(Id, cancellationToken));
            },
            cancellationToken);

    /// <summary>
    /// Opens the page where the user starts the download. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenPage))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => RunAsync(
            JobAction.OpenPage,
            () => _job.HandoffPage is { } page
                ? Pages.OpenAsync(page, _services.Browser, _services.Dialogs, Strings.OpenPageFailed, cancellationToken)
                : Task.CompletedTask,
            cancellationToken);

    /// <summary>
    /// Asks for the link the site handed over once the user started the download there, and hands it to the queue, which
    /// goes on with the download. A link to another file is refused, as is anything that isn't a link.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsAwaitingHandoff))]
    private Task PasteLinkAsync(CancellationToken cancellationToken)
        => RunAsync(
            JobAction.PasteLink,
            async () =>
            {
                TextQuestion question = new(
                    Strings.PasteLinkTitle,
                    Text.Format(Strings.PasteLinkMessage, FileName, SiteName),
                    Strings.PasteLinkHint,
                    Strings.PasteLinkConfirm);
                if (await _services.Dialogs.AskTextAsync(question, cancellationToken) is not { } text || string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                if (await HandOffAsync(text.Trim(), cancellationToken) is { } problem)
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.PasteLinkProblemTitle, problem, cancellationToken);
                }
            },
            cancellationToken);

    /// <summary>
    /// Hands <paramref name="text"/> to this download as its link, which the source checks: when it's the link the site
    /// handed over for the file, the download goes on with it.
    /// </summary>
    /// <returns>Why the link wasn't taken; or null when it was this download's.</returns>
    private async Task<string?> HandOffAsync(string text, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? link))
        {
            return Text.Format(Strings.PasteLinkNotALink, SiteName);
        }

        HandoffResult handed;
        try
        {
            handed = await _services.Links.HandOverAsync(Id, link, cancellationToken);
        }
        catch (Exception exception) when (LinkProblems.Describe(exception, library: false) is { } problem)
        {
            return problem;
        }

        return handed switch
        {
            { Answer: HandoffAnswer.Taken } => null,
            { Answer: HandoffAnswer.NotAHandoff } => Text.Format(Strings.PasteLinkNotTheHandoff, _job.FileName, SiteName),
            { OtherFileName: { } name } => Text.Format(Strings.PasteLinkToAnotherFile, name, _job.FileName),
            _ => Text.Format(Strings.PasteLinkToAnotherUnnamedFile, _job.FileName),
        };
    }

    /// <summary>
    /// Runs what the user asked for. Anything that goes wrong in it is logged, and the user told it didn't work, so a
    /// command never throws into the UI.
    /// </summary>
    private Task RunAsync(JobAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, Id), cancellationToken);
}
