using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// What each mod and file on the Browse screen uses, handed over by the screen as one.
/// </summary>
internal sealed class BrowseServices(
    IModLibrary library,
    ILibraryDownloads libraryDownloads,
    FolderDownloads folders,
    TrackedMods tracked,
    ISourceRegistry sources,
    IBrowserLauncher browser,
    IDialogService dialogs,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IModLibrary Library { get; } = library ?? throw new ArgumentNullException(nameof(library));

    public ILibraryDownloads LibraryDownloads { get; } = libraryDownloads ?? throw new ArgumentNullException(nameof(libraryDownloads));

    public FolderDownloads Folders { get; } = folders ?? throw new ArgumentNullException(nameof(folders));

    public TrackedMods Tracked { get; } = tracked ?? throw new ArgumentNullException(nameof(tracked));

    public ISourceRegistry Sources { get; } = sources ?? throw new ArgumentNullException(nameof(sources));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
