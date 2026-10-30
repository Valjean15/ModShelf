using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Links;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The download queue: a box to add a link, into the library or a folder the user picks, and every download, newest
/// first, as it goes. The badge counts the downloads waiting for the user: to be started on their site, or failed. A
/// link another app hands over, such as an <c>nxm://</c> link the system opens ModShelf with, is said here too.
/// </summary>
public sealed partial class DownloadsViewModel : ScreenViewModel
{
    private readonly IDownloadService _downloads;
    private readonly ILibraryDownloads _library;
    private readonly IOpenedLinks _opened;
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
        IOpenedLinks opened,
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
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _downloads = downloads;
        _library = library;
        _opened = opened;
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
                    Say(DownloadNotes.InLibraryAlready(placed));
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
    /// Downloads the file of a link another app handed over, such as an <c>nxm://</c> link from a website's "mod
    /// manager download" button, and says what became of it, as adding a link does: a download waiting for it goes on,
    /// otherwise the file goes into the library. The link in the box stays as it was.
    /// </summary>
    public Task OpenLinkAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        Message = null;
        return UserCommands.RunInPlaceAsync(
            async () =>
            {
                OpenedLink opened = await _opened.OpenAsync(link, cancellationToken);
                if (opened.Download is not { } added)
                {
                    Say(Text.Format(Strings.AddedInLibraryAlready, opened.LibraryPath));
                    return;
                }

                if (!added.AlreadySaved)
                {
                    _uiThread.Post(() => ShowNew(added.Job));
                }

                Say(DownloadNotes.Added(added, onDownloadsScreen: true));
            },
            exception => LinkProblems.Describe(exception, library: true),
            SayProblem,
            exception => LogOpenFailed(_logger, exception),
            cancellationToken);
    }

    /// <summary>
    /// Removes the finished and cancelled downloads from the queue and the list. Their files stay.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasFinished))]
    private Task ClearFinishedAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
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
            },
            _ => null,
            SayProblem,
            exception => LogClearFailed(_logger, exception),
            cancellationToken);

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
        await UserCommands.RunInPlaceAsync(
            async () =>
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
            },
            exception => LinkProblems.Describe(exception, library),
            SayProblem,
            exception => LogAddFailed(_logger, exception),
            cancellationToken);
        IsAdding = false;
    }

    /// <summary>
    /// Lists the job a link added, and says so. A job added earlier goes on, as adding its link again resumed or retried
    /// it. A file saved there already only says so.
    /// </summary>
    private void Follow(LinkDownload added)
    {
        if (!added.AlreadySaved)
        {
            _uiThread.Post(() => ShowNew(added.Job));
        }

        Link = string.Empty;
        Say(DownloadNotes.Added(added, onDownloadsScreen: true));
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
        DownloadJobViewModel added = new(job, _sources.SiteName(job.File.Mod.Game.Source), _jobServices);
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
