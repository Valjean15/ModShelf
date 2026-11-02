using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// What each mod on the Library screen uses, handed over by the screen as one.
/// </summary>
internal sealed class LibraryServices(
    IModLibrary library,
    ILibraryRescan rescan,
    ISettingsService settings,
    IBrowserLauncher browser,
    IFolderOpener folders,
    IDialogService dialogs,
    ModPictures pictures,
    Func<ModRef, CancellationToken, Task> open,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IModLibrary Library { get; } = library ?? throw new ArgumentNullException(nameof(library));

    public ILibraryRescan Rescan { get; } = rescan ?? throw new ArgumentNullException(nameof(rescan));

    public ISettingsService Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IFolderOpener Folders { get; } = folders ?? throw new ArgumentNullException(nameof(folders));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public ModPictures Pictures { get; } = pictures ?? throw new ArgumentNullException(nameof(pictures));

    /// <summary>
    /// Opens a mod on its page, in place of the list.
    /// </summary>
    public Func<ModRef, CancellationToken, Task> Open { get; } = open ?? throw new ArgumentNullException(nameof(open));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
