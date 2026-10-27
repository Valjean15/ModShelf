using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The download queue: a box to add a link, into the library or a folder the user picks, and every download, newest
/// first, as it goes. The badge counts the downloads waiting for the user: to be started on their site, or failed.
/// </summary>
public sealed partial class DownloadsViewModel : ScreenViewModel
{
    private readonly IDownloadService _downloads;
    private readonly ILibraryDownloads _library;
    private readonly ILinkDownloads _links;
    private readonly IDialogService _dialogs;
    private readonly ISourceRegistry _sources;
    private readonly IUiThread _uiThread;
    private readonly ILogger<DownloadsViewModel> _logger;
    private readonly DownloadJobServices _jobServices;
    private readonly Dictionary<DownloadJobId, DownloadJobViewModel> _shown = [];

    public DownloadsViewModel(
        IDownloadService downloads,
        ILibraryDownloads library,
        ILinkDownloads links,
        ISettingsService settings,
        IBrowserLauncher browser,
        IDialogService dialogs,
        ISourceRegistry sources,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<DownloadsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _downloads = downloads;
        _library = library;
        _links = links;
        _dialogs = dialogs;
        _sources = sources;
        _uiThread = uiThread;
        _logger = logger;
        _jobServices = new DownloadJobServices(downloads, library, links, settings, browser, dialogs, timeProvider, logger);
    }

    public override Screen Screen => Screen.Downloads;

    public override string Title => Strings.DownloadsTitle;

    /// <summary>
    /// The link in the box, as typed or pasted.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(AddToFolderCommand))]
    public partial string Link { get; set; } = string.Empty;

    /// <summary>
    /// What became of the last link added, such as where it's downloading to, or why it wasn't added; null for nothing
    /// to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// Whether <see cref="Message"/> says why a link wasn't added.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMessageAProblem { get; private set; }

    /// <summary>
    /// Whether a link is being added, which waits for its site to describe the file.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(AddToFolderCommand))]
    public partial bool IsAdding { get; private set; }

    /// <summary>
    /// Whether any download is listed; without one, the screen says how to start.
    /// </summary>
    [ObservableProperty]
    public partial bool HasJobs { get; private set; }

    /// <summary>
    /// Whether any download listed is done or cancelled, so there's something to clear.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFinishedCommand))]
    public partial bool HasFinished { get; private set; }

    /// <summary>
    /// Every download, newest first.
    /// </summary>
    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = [];

    /// <summary>
    /// Lists the queue's downloads, then follows every change to them while the app runs.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Watching first and listing after, so no change from here on is missed.
        IAsyncEnumerable<DownloadJobSnapshot> changes = _downloads.WatchAsync(cancellationToken);
        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken);
        _uiThread.Post(() =>
        {
            foreach (DownloadJobSnapshot job in jobs)
            {
                Show(job);
            }
        });

        await foreach (DownloadJobSnapshot job in changes)
        {
            _uiThread.Post(() => Show(job));
        }
    }

    private bool CanAdd() => !IsAdding && !string.IsNullOrWhiteSpace(Link);

    /// <summary>
    /// Downloads the link's file into the library, where its game's folder pattern puts it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync(CancellationToken cancellationToken)
        => AddingAsync(
            library: true,
            async link =>
            {
                LibraryDownload placed = await _library.AddAsync(link, cancellationToken);
                if (placed.Queued is null)
                {
                    Say(Text.Format(Strings.DownloadsInLibraryAlready, placed.Path));
                    return null;
                }

                return placed.Queued;
            },
            cancellationToken);

    /// <summary>
    /// Downloads the link's file into a folder the user picks, outside the library, under the name its site gives it;
    /// or, when the link doesn't tell, under a name the user types.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddToFolderAsync(CancellationToken cancellationToken)
        => AddingAsync(
            library: false,
            async link =>
            {
                if (await _dialogs.PickFolderAsync(Strings.ChooseFolderTitle, cancellationToken) is not { } picked)
                {
                    return null;
                }

                string folder = Path.TrimEndingDirectorySeparator(picked);
                try
                {
                    return await _links.AddAsync(link, folder, null, cancellationToken);
                }
                catch (ArgumentException exception) when (exception.ParamName == "fileName")
                {
                    TextQuestion question = new(Strings.NameTheFileTitle, Strings.NameTheFileMessage, Strings.NameTheFileHint, Strings.NameTheFileConfirm);
                    return await _dialogs.AskTextAsync(question, cancellationToken) is { } name && !string.IsNullOrWhiteSpace(name)
                        ? await _links.AddAsync(link, folder, name.Trim(), cancellationToken)
                        : null;
                }
            },
            cancellationToken);

    /// <summary>
    /// Removes the finished and cancelled downloads from the queue and the list. Their files stay.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasFinished))]
    private async Task ClearFinishedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _downloads.ClearFinishedAsync(cancellationToken);
            HashSet<DownloadJobId> left = [.. (await _downloads.ListAsync(cancellationToken)).Select(job => job.Id)];

            // Only finished ones, so a download added meanwhile, and not listed yet, stays.
            _uiThread.Post(() =>
            {
                foreach (DownloadJobViewModel job in Jobs.Where(job => job.IsFinished && !left.Contains(job.Id)).ToList())
                {
                    Jobs.Remove(job);
                    _shown.Remove(job.Id);
                }

                Count();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception)
        {
            LogClearFailed(_logger, exception);
            SayProblem(Strings.DownloadsUnexpected);
        }
    }

    /// <summary>
    /// Adds the link in the box, and says what became of it.
    /// </summary>
    /// <param name="library">Whether the file goes into the library.</param>
    /// <param name="add">Adds the link; returns the job that downloads its file, or null when there's none to follow.</param>
    private async Task AddingAsync(bool library, Func<Uri, Task<LinkDownload?>> add, CancellationToken cancellationToken)
    {
        Message = null;
        if (!Uri.TryCreate(Link.Trim(), UriKind.Absolute, out Uri? link))
        {
            SayProblem(Strings.DownloadsNotALink);
            return;
        }

        IsAdding = true;
        try
        {
            if (await add(link) is { } added)
            {
                await FollowAsync(added, cancellationToken);
            }
            else if (Message is not null)
            {
                // In the library already, so the link is done with. Without a folder or a name picked, it stays for another go.
                Link = string.Empty;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (LinkProblems.Describe(exception, library) is { } problem)
        {
            SayProblem(problem);
        }
        catch (Exception exception)
        {
            LogAddFailed(_logger, exception);
            SayProblem(Strings.DownloadsUnexpected);
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Lists the job a link added, and says so. A job added earlier goes on: a paused one resumes, and a failed one is
    /// retried.
    /// </summary>
    private async Task FollowAsync(LinkDownload added, CancellationToken cancellationToken)
    {
        DownloadJobSnapshot job = added.Job;
        _uiThread.Post(() => ShowNew(job));
        Link = string.Empty;
        if (!added.AlreadyQueued)
        {
            Say(Text.Format(Strings.DownloadsDownloadingInto, job.FileName, job.TargetDirectory));
            return;
        }

        Say(Text.Format(Strings.DownloadsContinuing, job.FileName));
        try
        {
            if (job.Status == DownloadJobStatus.Paused)
            {
                await _downloads.ResumeAsync(job.Id, cancellationToken);
            }
            else if (job.Status == DownloadJobStatus.Failed)
            {
                await _downloads.RetryAsync(job.Id, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            // It went on meanwhile, or was cleared.
        }
    }

    /// <summary>
    /// Shows the job as <paramref name="job"/> has it, adding it to the list when it's new. Runs on the UI thread.
    /// </summary>
    private void Show(DownloadJobSnapshot job)
    {
        if (_shown.TryGetValue(job.Id, out DownloadJobViewModel? shown))
        {
            shown.Show(job);
        }
        else
        {
            Add(job);
        }

        Count();
    }

    /// <summary>
    /// Adds a job that a link added, unless the queue has shown it already: what the queue showed is newer.
    /// </summary>
    private void ShowNew(DownloadJobSnapshot job)
    {
        if (!_shown.ContainsKey(job.Id))
        {
            Add(job);
            Count();
        }
    }

    private void Add(DownloadJobSnapshot job)
    {
        string site = _sources.Find<IModSource>(job.File.Mod.Game.Source)?.DisplayName ?? job.File.Mod.Game.Source.Value;
        DownloadJobViewModel added = new(job, site, _jobServices);
        _shown.Add(job.Id, added);

        // Newest first: before the first job added no later than this one.
        int index = 0;
        while (index < Jobs.Count && Jobs[index].CreatedAt > job.CreatedAt)
        {
            index++;
        }

        Jobs.Insert(index, added);
    }

    /// <summary>
    /// Counts what the screen's badge, the empty list and the clear button show.
    /// </summary>
    private void Count()
    {
        Badge = Jobs.Count(job => job.IsAwaitingHandoff || job.IsFailed);
        HasJobs = Jobs.Count > 0;
        HasFinished = Jobs.Any(job => job.IsFinished);
    }

    private void Say(string message)
    {
        Message = message;
        IsMessageAProblem = false;
    }

    private void SayProblem(string message)
    {
        Message = message;
        IsMessageAProblem = true;
    }
}
