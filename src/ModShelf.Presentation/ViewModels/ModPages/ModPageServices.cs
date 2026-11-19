using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Contents;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Requirements;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.Undo;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// What a mod's page and its files use, handed over by the screen that shows it as one.
/// </summary>
internal sealed class ModPageServices(
    IModLibrary library,
    ILibraryDownloads libraryDownloads,
    ILibraryRescan rescan,
    ILibraryStorage storage,
    ILibraryContents contents,
    ISettingsService settings,
    FolderDownloads folders,
    TrackedMods tracked,
    EndorsedMods endorsed,
    IRequirementDownloads requirements,
    ISourceRegistry sources,
    IBrowserLauncher browser,
    IFolderOpener folderOpener,
    IClipboard clipboard,
    IDialogService dialogs,
    ModPictures pictures,
    UndoBarViewModel undo,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IModLibrary Library { get; } = library ?? throw new ArgumentNullException(nameof(library));

    public ILibraryDownloads LibraryDownloads { get; } = libraryDownloads ?? throw new ArgumentNullException(nameof(libraryDownloads));

    public ILibraryRescan Rescan { get; } = rescan ?? throw new ArgumentNullException(nameof(rescan));

    public ILibraryStorage Storage { get; } = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>
    /// What the library's archives hold (ADR 0106).
    /// </summary>
    public ILibraryContents Contents { get; } = contents ?? throw new ArgumentNullException(nameof(contents));

    public ISettingsService Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public FolderDownloads Folders { get; } = folders ?? throw new ArgumentNullException(nameof(folders));

    public TrackedMods Tracked { get; } = tracked ?? throw new ArgumentNullException(nameof(tracked));

    /// <summary>
    /// What the linked accounts said of mods on the sites that let ModShelf endorse.
    /// </summary>
    public EndorsedMods Endorsed { get; } = endorsed ?? throw new ArgumentNullException(nameof(endorsed));

    /// <summary>
    /// What a mod needs, planned for downloading with it.
    /// </summary>
    public IRequirementDownloads Requirements { get; } = requirements ?? throw new ArgumentNullException(nameof(requirements));

    public ISourceRegistry Sources { get; } = sources ?? throw new ArgumentNullException(nameof(sources));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IFolderOpener FolderOpener { get; } = folderOpener ?? throw new ArgumentNullException(nameof(folderOpener));

    public IClipboard Clipboard { get; } = clipboard ?? throw new ArgumentNullException(nameof(clipboard));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public ModPictures Pictures { get; } = pictures ?? throw new ArgumentNullException(nameof(pictures));

    /// <summary>
    /// The bar that offers to undo a change.
    /// </summary>
    public UndoBarViewModel Undo { get; } = undo ?? throw new ArgumentNullException(nameof(undo));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
