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
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The download queue: a box to add a link, into the library or a folder the user picks, a summary of what's under way,
/// and every download, newest first, as it goes, by tab: all, under way, waiting for the user, failed or finished; with
/// previews, each shows its mod's thumbnail. Downloads waiting for the user to start them on their site are said above
/// the list, with a way to open the next one's page. The badge counts the downloads waiting for the user: to be started
/// on their site, or failed. A link another app hands over, such as an <c>nxm://</c> link the system opens ModShelf
/// with, is said here too.
/// </summary>
public sealed partial class DownloadsViewModel : ScreenViewModel
{
    private readonly IDownloadService _downloads;
    private readonly ILibraryDownloads _library;
    private readonly IOpenedLinks _opened;
    private readonly FolderDownloads _folders;
    private readonly ISourceRegistry _sources;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly ILogger<DownloadsViewModel> _logger;
    private readonly DownloadJobServices _jobServices;
    private readonly Dictionary<DownloadJobId, DownloadJobViewModel> _shown = [];
    private CancellationToken _stopping;
    private bool _picturesShown;

    public DownloadsViewModel(
        IDownloadService downloads,
        ILibraryDownloads library,
        ILinkDownloads links,
        IOpenedLinks opened,
        ISettingsService settings,
        IBrowserLauncher browser,
        IDialogService dialogs,
        ISourceRegistry sources,
        ModPictures pictures,
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
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _downloads = downloads;
        _library = library;
        _opened = opened;
        _folders = new FolderDownloads(links, dialogs);
        _sources = sources;
        _pictures = pictures;
        _uiThread = uiThread;
        _logger = logger;
        _jobServices = new DownloadJobServices(downloads, links, browser, dialogs, timeProvider, logger);
        Tabs =
        [
            new(DownloadTab.All, Strings.DownloadsTabAll),
            new(DownloadTab.UnderWay, Strings.DownloadsTabUnderWay),
            new(DownloadTab.WaitingForYou, Strings.DownloadsTabWaitingForYou),
            new(DownloadTab.Failed, Strings.DownloadsTabFailed),
            new(DownloadTab.Finished, Strings.DownloadsTabFinished),
        ];
        SelectedTab = Tabs[0];
        Previews = new PreviewsViewModel(PreviewScreen.Downloads, pictures);
        Previews.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PreviewsViewModel.Level))
            {
                ShowPictures();
            }
        };
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
    /// Whether the downloads show their mods' thumbnails.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// Every download, newest first.
    /// </summary>
    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = [];

    /// <summary>
    /// The tabs over the list, each counting its downloads.
    /// </summary>
    public IReadOnlyList<DownloadTabViewModel> Tabs { get; }

    /// <summary>
    /// The tab shown; all downloads at first.
    /// </summary>
    [ObservableProperty]
    public partial DownloadTabViewModel SelectedTab { get; set; }

    /// <summary>
    /// The downloads the tab shows, newest first.
    /// </summary>
    public ObservableCollection<DownloadJobViewModel> ShownJobs { get; } = [];

    /// <summary>
    /// Whether the tab shows none, though there are downloads.
    /// </summary>
    [ObservableProperty]
    public partial bool NothingInTab { get; private set; }

    /// <summary>
    /// Such as "2 downloading at 4.10 MB/s, 1 queued · 2 waiting for you · 1 failed"; empty without downloads.
    /// </summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "2 files wait for you on Nexus Mods. …"; empty when none does.
    /// </summary>
    [ObservableProperty]
    public partial string WaitingText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasWaiting { get; private set; }

    /// <summary>
    /// Whether a download waiting for the user has its page to open.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenNextPageCommand))]
    public partial bool CanOpenNextPage { get; private set; }

    /// <summary>
    /// Reads the previews from the settings, lists the queue's downloads, then follows every change to them while the app
    /// runs. While another process runs the queue, it says so, and lists the downloads again once they run here.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        await _pictures.LoadAsync();
        Previews.Refresh();
        ShowPictures();
        _pictures.Changed += (_, _) => _uiThread.Post(() =>
        {
            Previews.Refresh();
            ShowPictures();
        });

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

    partial void OnSelectedTabChanged(DownloadTabViewModel value) => Count();

    /// <summary>
    /// Opens the page of the download that has waited longest for the user, where they start it. One page a click: the
    /// next opens with the next click.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenNextPage))]
    private Task OpenNextPageAsync()
        => Jobs.Where(job => job.CanOpenPage).OrderBy(job => job.CreatedAt).FirstOrDefault() is { } next
            ? next.OpenPageCommand.ExecuteAsync(null)
            : Task.CompletedTask;

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
        DownloadJobViewModel added = new(job, _sources.SiteName(job.File.Mod.Game.Source), _jobServices, _pictures);
        _shown.Add(job.Id, added);
        if (_picturesShown)
        {
            added.Picture.Show(added.Mod, _stopping);
        }

        // Newest first: before the first job added no later than this one.
        int index = 0;
        while (index < Jobs.Count && Jobs[index].CreatedAt > job.CreatedAt)
        {
            index++;
        }

        Jobs.Insert(index, added);
    }

    /// <summary>
    /// Shows each download's thumbnail while the screen shows them, as adult mods' pictures should look now; hides them
    /// otherwise. A picture already shown isn't read again.
    /// </summary>
    private void ShowPictures()
    {
        bool shown = Previews.ShowsPictures;
        foreach (DownloadJobViewModel job in Jobs)
        {
            if (!shown)
            {
                job.Picture.Hide();
            }
            else if (_picturesShown)
            {
                job.Picture.Refresh(_stopping);
            }
            else
            {
                job.Picture.Show(job.Mod, _stopping);
            }
        }

        _picturesShown = shown;
    }

    /// <summary>
    /// Counts what the screen's badge, summary, tabs, the downloads waiting for the user, the empty list and the clear
    /// button show, and shows the selected tab's downloads.
    /// </summary>
    private void Count()
    {
        Badge = Jobs.Count(job => job.IsAwaitingHandoff || job.IsFailed);
        HasJobs = Jobs.Count > 0;
        HasFinished = Jobs.Any(job => job.IsFinished);
        foreach (DownloadTabViewModel tab in Tabs)
        {
            tab.Count = Jobs.Count(job => Shows(tab.Tab, job));
        }

        ShownJobs.SyncWith([.. Jobs.Where(job => Shows(SelectedTab?.Tab ?? DownloadTab.All, job))]);
        NothingInTab = HasJobs && ShownJobs.Count == 0;
        SummaryText = Summarize();

        DownloadJobViewModel[] waiting = [.. Jobs.Where(job => job.IsAwaitingHandoff)];
        string[] sites = [.. waiting.Select(job => job.SiteName).Distinct(StringComparer.Ordinal)];
        HasWaiting = waiting.Length > 0;
        WaitingText = sites switch
        {
            [] => string.Empty,
            [{ } site] => waiting.Length == 1
                ? Text.Format(Strings.DownloadsWaitingOne, site)
                : Text.Format(Strings.DownloadsWaiting, waiting.Length, site),
            _ => Text.Format(Strings.DownloadsWaitingOnSites, waiting.Length),
        };
        CanOpenNextPage = waiting.Any(job => job.CanOpenPage);
    }

    private static bool Shows(DownloadTab tab, DownloadJobViewModel job) => tab == DownloadTab.All || job.Tab == tab;

    /// <summary>
    /// Such as "2 downloading at 4.10 MB/s, 1 queued · 2 waiting for you · 1 failed": what's under way, then what waits for
    /// the user. "Nothing under way" when every download is finished; empty without downloads.
    /// </summary>
    private string Summarize()
    {
        if (Jobs.Count == 0)
        {
            return string.Empty;
        }

        DownloadJobViewModel[] downloading = [.. Jobs.Where(job => job.Status is DownloadJobStatus.Resolving or DownloadJobStatus.Downloading or DownloadJobStatus.Verifying)];
        long speed = downloading.Sum(job => job.BytesPerSecond);
        int queued = Jobs.Count(job => job.Status == DownloadJobStatus.Queued);
        int paused = Jobs.Count(job => job.Status == DownloadJobStatus.Paused);
        List<string> underWay = [];
        if (downloading.Length > 0)
        {
            underWay.Add(speed > 0
                ? Text.Format(Strings.DownloadsSummaryDownloadingAt, downloading.Length, Sizes.Format(speed))
                : Text.Format(Strings.DownloadsSummaryDownloading, downloading.Length));
        }

        if (queued > 0)
        {
            underWay.Add(Text.Format(Strings.DownloadsSummaryQueued, queued));
        }

        if (paused > 0)
        {
            underWay.Add(Text.Format(Strings.DownloadsSummaryPaused, paused));
        }

        List<string> parts = [];
        if (underWay.Count > 0)
        {
            parts.Add(string.Join(Strings.ListJoin, underWay));
        }

        int waiting = Jobs.Count(job => job.IsAwaitingHandoff);
        if (waiting > 0)
        {
            parts.Add(Text.Format(Strings.DownloadsSummaryWaiting, waiting));
        }

        int failed = Jobs.Count(job => job.IsFailed);
        if (failed > 0)
        {
            parts.Add(Text.Format(Strings.DownloadsSummaryFailed, failed));
        }

        return parts.Count == 0 ? Strings.DownloadsSummaryIdle : string.Join(Strings.SummaryJoin, parts);
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
