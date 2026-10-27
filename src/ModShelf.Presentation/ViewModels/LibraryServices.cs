using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// What each mod and file on the Library screen uses, handed over by the screen as one.
/// </summary>
internal sealed class LibraryServices(
    IModLibrary library,
    ISettingsService settings,
    IBrowserLauncher browser,
    IFolderOpener folders,
    IDialogService dialogs,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IModLibrary Library { get; } = library ?? throw new ArgumentNullException(nameof(library));

    public ISettingsService Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IFolderOpener Folders { get; } = folders ?? throw new ArgumentNullException(nameof(folders));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
