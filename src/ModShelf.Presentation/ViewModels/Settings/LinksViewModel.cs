using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Links;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Links from websites, on the Settings screen: for each site that hands its downloads over with links of its own, such
/// as Nexus Mods' <c>nxm://</c> links, which app opens them. It reads them again each time the Settings screen is shown,
/// since another app, such as Vortex as it starts, can take them meanwhile.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject, IDisposable
{
    private readonly ILinkHandlers _handlers;
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;
    private readonly ILogger<LinksViewModel> _logger;

    public LinksViewModel(ILinkHandlers handlers, INavigationService navigation, IUiThread uiThread, ILogger<LinksViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _handlers = handlers;
        _navigation = navigation;
        _uiThread = uiThread;
        _logger = logger;
        navigation.Navigated += OnNavigated;
    }

    /// <summary>
    /// Each site, in the order the sources are registered.
    /// </summary>
    public ObservableCollection<LinkHandlerViewModel> Sites { get; } = [];

    /// <summary>
    /// Why the sites' links couldn't be read; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    public void Dispose() => _navigation.Navigated -= OnNavigated;

    /// <summary>
    /// Reads which app opens each site's links, and shows it. Runs on the UI thread.
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                IReadOnlyList<LinkHandler> handlers = await _handlers.ListAsync(cancellationToken);
                Problem = null;
                for (int index = 0; index < handlers.Count; index++)
                {
                    if (index < Sites.Count && Sites[index].Handler.Source == handlers[index].Source)
                    {
                        Sites[index].Show(handlers[index]);
                    }
                    else
                    {
                        Sites.Insert(index, new LinkHandlerViewModel(
                            handlers[index],
                            _handlers,
                            exception => LogTakeFailed(_logger, exception),
                            exception => LogGiveBackFailed(_logger, exception)));
                    }
                }

                while (Sites.Count > handlers.Count)
                {
                    Sites.RemoveAt(Sites.Count - 1);
                }
            },
            exception => exception is IOException or UnauthorizedAccessException ? Text.Format(Strings.LinksCantRead, exception.Message) : null,
            problem => Problem = problem,
            exception => LogLoadFailed(_logger, exception),
            cancellationToken);

    private void OnNavigated(object? sender, Screen screen)
    {
        if (screen == Screen.Settings)
        {
            _uiThread.Post(() => _ = LoadAsync(CancellationToken.None));
        }
    }
}
