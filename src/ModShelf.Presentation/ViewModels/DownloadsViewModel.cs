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
    private readonly FolderDownloads _folders;
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
        _folders = new FolderDownloads(links, dialogs);
        _sources = sources;
        _uiThread = uiThread;
        _logger = logger;
        _jobServices = new DownloadJobServices(downloads, links, browser, dialogs, timeProvider, logger);
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
    /// Whether another ModShelf process, such as the command line, runs the downloads now, so nothing downloads here and
    /// no download can be added or changed until it ends.
    /// </summary>
    [ObservableProperty]
    public partial bool IsQueueBusyElsewhere { get; private set; }

    /// <summary>
    /// Every download, newest first.
    /// </summary>
    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = [];

    /// <summary>
    /// Lists the queue's downloads, then follows every change to them while the app runs. While another process runs
    /// the queue, it says so, and lists the downloads again once they run here.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Watching first and listing after, so no change from here on is missed.
        IAsyncEnumerable<DownloadJobSnapshot> changes = _downloads.WatchAsync(cancellationToken);
        bool busyElsewhere = _downloads.IsQueueBusyElsewhere;
        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken);
        _uiThread.Post(() =>
        {
            IsQueueBusyElsewhere = busyElsewhere;
            foreach (DownloadJobSnapshot job in jobs)
            {
                Show(job);
            }
        });

        Task runsHere = busyElsewhere ? ListOnceTheQueueRunsHereAsync(cancellationToken) : Task.CompletedTask;
        await foreach (DownloadJobSnapshot job in changes)
        {
            _uiThread.Post(() => Show(job));
        }

        await runsHere;
    }

    private bool CanAdd() => !IsAdding && !string.IsNullOrWhiteSpace(Link);

    /// <summary>
    /// Once the other process lets go of the queue, lists the downloads as this one loaded them.
    /// </summary>
    private async Task ListOnceTheQueueRunsHereAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _downloads.WhenQueueRunsHereAsync(cancellationToken);
            IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken);
            _uiThread.Post(() =>
            {
                IsQueueBusyElsewhere = false;
                foreach (DownloadJobSnapshot job in jobs)
                {
                    Show(job);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
    }

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
            link => _folders.AddAsync(link, cancellationToken),
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
            SayProblem(Strings.SomethingWentWrong);
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
                Follow(added);
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
            SayProblem(Strings.SomethingWentWrong);
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Lists the job a link added, and says so. A job added earlier goes on, as adding its link again resumed or retried
    /// it. A file saved there already only says so.
    /// </summary>
    private void Follow(LinkDownload added)
    {
        DownloadJobSnapshot job = added.Job;
        if (added.AlreadySaved)
        {
            Link = string.Empty;
            Say(Text.Format(Strings.DownloadsSavedAlready, job.FileName, job.TargetDirectory));
            return;
        }

        _uiThread.Post(() => ShowNew(job));
        Link = string.Empty;
        if (!added.AlreadyQueued)
        {
            Say(Text.Format(Strings.DownloadsDownloadingInto, job.FileName, job.TargetDirectory));
            return;
        }

        Say(Text.Format(Strings.DownloadsContinuing, job.FileName));
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
