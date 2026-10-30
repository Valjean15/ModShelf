using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One mod in the library: its name and author, whether it's watched, its files and updates, and what can be done with
/// it.
/// </summary>
public sealed partial class LibraryModViewModel : ObservableObject
{
    private readonly LibraryServices _services;
    private readonly Dictionary<ModFileRef, LibraryFileViewModel> _files = [];
    private LibraryMod _mod;

    internal LibraryModViewModel(LibraryMod mod, LibraryServices services)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(services);

        _mod = mod;
        _services = services;
        Mod = mod.Mod;
        Show(mod);
    }

    /// <summary>
    /// What the user asked of a mod, as the log names it.
    /// </summary>
    private enum ModAction
    {
        OpenPage,
        OpenFolder,
        Watch,
        Unwatch,
    }

    public ModRef Mod { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "by schlangster"; empty when the site names no author.
    /// </summary>
    [ObservableProperty]
    public partial string AuthorText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WatchCommand), nameof(UnwatchCommand))]
    public partial bool IsWatched { get; private set; }

    /// <summary>
    /// "Watched for updates" or "Not watched".
    /// </summary>
    [ObservableProperty]
    public partial string WatchedText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "2 files", or "No files".
    /// </summary>
    [ObservableProperty]
    public partial string FilesText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial bool HasFiles { get; private set; }

    /// <summary>
    /// Such as "1 update"; empty without updates.
    /// </summary>
    [ObservableProperty]
    public partial string UpdatesText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUpdates { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPageCommand))]
    public partial bool HasPage { get; private set; }

    /// <summary>
    /// Whether its files are shown, kept here so the list can reuse its rows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Its files, the latest downloaded first.
    /// </summary>
    public ObservableCollection<LibraryFileViewModel> Files { get; } = [];

    /// <summary>
    /// Shows the mod as the library has it now. Runs on the UI thread.
    /// </summary>
    internal void Show(LibraryMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (mod.Mod != Mod)
        {
            throw new ArgumentException("That's another mod.", nameof(mod));
        }

        _mod = mod;
        Name = mod.Name;
        AuthorText = mod.Author is { } author ? Text.Format(Strings.ModByAuthor, author) : string.Empty;
        IsWatched = mod.IsWatched;
        WatchedText = mod.IsWatched ? Strings.LibraryWatched : Strings.LibraryNotWatched;
        FilesText = Counts.Of(mod.Files.Count, Strings.LibraryNoFiles, Strings.LibraryOneFile, Strings.LibraryFiles);
        HasFiles = mod.Files.Count > 0;
        UpdatesText = Counts.Of(mod.Updates.Count, string.Empty, Strings.LibraryOneUpdate, Strings.LibraryUpdates);
        HasUpdates = mod.Updates.Count > 0;
        HasPage = mod.PageUrl is not null;

        List<LibraryFileViewModel> files = [];
        foreach (LibraryFile file in mod.Files.OrderByDescending(file => file.DownloadedAt))
        {
            if (_files.TryGetValue(file.File, out LibraryFileViewModel? shown))
            {
                shown.Show(file);
            }
            else
            {
                shown = new LibraryFileViewModel(file, _services);
                _files.Add(file.File, shown);
            }

            files.Add(shown);
        }

        foreach (ModFileRef gone in _files.Keys.Except(mod.Files.Select(file => file.File)).ToList())
        {
            _files.Remove(gone);
        }

        Files.SyncWith(files);
    }

    /// <summary>
    /// Opens the mod's page on its site. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPage))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => RunAsync(
            ModAction.OpenPage,
            () => _mod.PageUrl is { } page
                ? Pages.OpenAsync(page, _services.Browser, _services.Dialogs, Strings.OpenPageByHand, cancellationToken)
                : Task.CompletedTask,
            cancellationToken);

    /// <summary>
    /// Opens the folder of the file downloaded last.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasFiles))]
    private Task OpenFolderAsync(CancellationToken cancellationToken)
        => RunAsync(
            ModAction.OpenFolder,
            async () =>
            {
                if (_mod.Files.MaxBy(file => file.DownloadedAt) is not { } latest)
                {
                    return;
                }

                string root;
                try
                {
                    root = await _services.Settings.GetLibraryRootAsync(cancellationToken);
                }
                catch (InvalidDataException exception)
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.OpenFolderFailedTitle, exception.Message, cancellationToken);
                    return;
                }

                string folder = Path.GetDirectoryName(LibraryPaths.Resolve(root, latest.RelativePath))!;
                if (!await _services.Folders.TryOpenAsync(folder, cancellationToken))
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.OpenFolderFailedTitle, Text.Format(Strings.OpenFolderFailed, folder), cancellationToken);
                }
            },
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanWatch))]
    private Task WatchAsync(CancellationToken cancellationToken) => SetWatchedAsync(ModAction.Watch, true, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsWatched))]
    private Task UnwatchAsync(CancellationToken cancellationToken) => SetWatchedAsync(ModAction.Unwatch, false, cancellationToken);

    private bool CanWatch() => !IsWatched;

    /// <summary>
    /// Watches or unwatches the mod. The screen shows the change once the library makes it; a mod unwatched without
    /// files leaves it.
    /// </summary>
    private Task SetWatchedAsync(ModAction action, bool watched, CancellationToken cancellationToken)
        => RunAsync(
            action,
            async () =>
            {
                try
                {
                    await _services.Library.SetWatchedAsync(Mod, watched, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
            },
            cancellationToken);

    private Task RunAsync(ModAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, Mod.Id, Mod.Game.Key), cancellationToken);
}
