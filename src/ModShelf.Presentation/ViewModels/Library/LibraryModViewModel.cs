using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One mod in the library, as a row or card of its game's mods: its name and author, your version, what to know of it
/// (updates, files missing from disk, whether it's watched), its files and the space they take, when its site last
/// updated it, and what can be done with it. Its files are on its page.
/// </summary>
public sealed partial class LibraryModViewModel : ObservableObject
{
    private readonly LibraryServices _services;
    private LibraryMod _mod;

    internal LibraryModViewModel(LibraryMod mod, LibraryServices services)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(services);

        _mod = mod;
        _services = services;
        Mod = mod.Mod;
        Picture = new ModPictureViewModel(services.Pictures);
        UpdatedText = Strings.NoValue;
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
        Remove,
        Rescan,
        Pin,
        Unpin,
        Note,
        OfferSkipped,
    }

    public ModRef Mod { get; }

    /// <summary>
    /// Its picture, as its thumbnail or card shows it, while the screen shows pictures.
    /// </summary>
    public ModPictureViewModel Picture { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectText))]
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
    /// Whether the user pinned it at the version the library has, so its updates aren't offered.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PinCommand), nameof(UnpinCommand))]
    public partial bool IsPinned { get; private set; }

    /// <summary>
    /// The user's note of it; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(EditNoteText))]
    public partial string? Note { get; private set; }

    public bool HasNote => Note is not null;

    /// <summary>
    /// Whether the user skipped some of its updates, which can be offered again.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OfferSkippedCommand))]
    public partial bool HasSkipped { get; private set; }

    /// <summary>
    /// "Add a note…", or "Edit the note…" when it has one.
    /// </summary>
    public string EditNoteText => HasNote ? Strings.ModEditNote : Strings.ModAddNote;

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
    [NotifyCanExecuteChangedFor(nameof(OpenPageCommand), nameof(RescanCommand))]
    public partial bool HasPage { get; private set; }

    /// <summary>
    /// The version of your newest file, a main file when there is one, archived versions aside; "—" for none.
    /// </summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = string.Empty;

    /// <summary>
    /// The space its files take, archived versions included; "—" without files.
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// The space its files take, in bytes, archived versions included.
    /// </summary>
    public long Size { get; private set; }

    /// <summary>
    /// The space its archived versions take, in bytes.
    /// </summary>
    public long ArchivedSize { get; private set; }

    /// <summary>
    /// How many of its files are old versions in an <c>_archive</c> folder.
    /// </summary>
    public int ArchivedCount { get; private set; }

    /// <summary>
    /// How many of its files aren't on disk where the library says they are; 0 until they're checked.
    /// </summary>
    public int MissingCount { get; private set; }

    /// <summary>
    /// When it came into the library.
    /// </summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>
    /// When its updates were last checked; null when they never were.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>
    /// How many updates were found for it.
    /// </summary>
    public int UpdateCount { get; private set; }

    /// <summary>
    /// Whether it's selected, for Update selected; the screen unselects it when it's no longer shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Such as "Select SkyUI", for screen readers.
    /// </summary>
    public string SelectText => Text.Format(Strings.SelectMod, Name);

    /// <summary>
    /// Its updates whose files can be downloaded, by its site's link to each, the newest first.
    /// </summary>
    internal IReadOnlyList<LibraryUpdate> DownloadableUpdates { get; private set; } = [];

    /// <summary>
    /// When its site last updated it, as its saved details say; null until they're read, or when they don't say.
    /// </summary>
    public DateTimeOffset? SiteUpdatedAt { get; private set; }

    /// <summary>
    /// Such as "2 days ago": when its site last updated it; "—" when that isn't known.
    /// </summary>
    [ObservableProperty]
    public partial string UpdatedText { get; private set; } = string.Empty;

    /// <summary>
    /// What to know of it, such as "Update to 1.4", "1 file missing" or "Watched only"; empty when there's nothing.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ModStatus> Statuses { get; private set; } = [];

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
        LibraryFile[] archived = [.. mod.Files.Where(file => OwnFileViewModel.IsInArchive(file.RelativePath))];
        int current = mod.Files.Count - archived.Length;
        string filesText = Counts.Of(current, Strings.LibraryNoFiles, Strings.LibraryOneFile, Strings.LibraryFiles);
        FilesText = archived.Length > 0 ? Text.Format(Strings.LibraryFilesArchived, filesText, archived.Length) : filesText;
        HasFiles = mod.Files.Count > 0;
        // A pinned mod's updates, and the ones skipped, aren't offered: not counted, shown, or downloaded with the others.
        IReadOnlyList<LibraryUpdate> offered = mod.OfferedUpdates;
        UpdateCount = offered.Count;
        DownloadableUpdates = [.. offered.Where(update => update.PageUrl is not null).OrderByDescending(update => update.UploadedAt)];
        UpdatesText = Counts.Of(offered.Count, string.Empty, Strings.LibraryOneUpdate, Strings.LibraryUpdates);
        HasUpdates = offered.Count > 0;
        IsPinned = mod.IsPinned;
        Note = mod.Note;
        HasSkipped = mod.SkippedUpdates.Count > 0;
        HasPage = mod.PageUrl is not null;
        ArchivedCount = archived.Length;
        ArchivedSize = archived.Sum(file => file.SizeInBytes);
        Size = mod.Files.Sum(file => file.SizeInBytes);
        SizeText = HasFiles ? Sizes.Format(Size) : Strings.NoValue;
        AddedAt = mod.AddedAt;
        CheckedAt = mod.CheckedAt;
        LibraryFile? yours = mod.Files
            .Where(file => !OwnFileViewModel.IsInArchive(file.RelativePath))
            .OrderByDescending(file => file.Category == ModFileCategory.Main)
            .ThenByDescending(file => file.DownloadedAt)
            .FirstOrDefault();
        VersionText = yours?.VersionLabel ?? Strings.NoValue;
        ShowStatuses();
    }

    partial void OnIsSelectedChanged(bool value) => _services.SelectionChanged();

    /// <summary>
    /// Shows how many of its files aren't on disk. Runs on the UI thread.
    /// </summary>
    internal void ShowMissing(int count)
    {
        MissingCount = count;
        ShowStatuses();
    }

    /// <summary>
    /// Shows when its site last updated it, from its saved details. Runs on the UI thread.
    /// </summary>
    internal void ShowSiteUpdate(DateTimeOffset? updatedAt)
    {
        SiteUpdatedAt = updatedAt;
        UpdatedText = updatedAt is { } updated ? Dates.Ago(updated, _services.TimeProvider) : Strings.NoValue;
    }

    /// <summary>
    /// Opens the mod on its page, in place of the list.
    /// </summary>
    [RelayCommand]
    private Task OpenAsync(CancellationToken cancellationToken) => _services.Open(Mod, cancellationToken);

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

    /// <summary>
    /// Pins the mod at the version the library has, so its updates aren't offered.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPin))]
    private Task PinAsync(CancellationToken cancellationToken) => SetPinnedAsync(ModAction.Pin, true, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsPinned))]
    private Task UnpinAsync(CancellationToken cancellationToken) => SetPinnedAsync(ModAction.Unpin, false, cancellationToken);

    /// <summary>
    /// Asks for the mod's note, starting with the one it has; an empty one removes it. The screen shows the change once
    /// the library makes it.
    /// </summary>
    [RelayCommand]
    private Task EditNoteAsync(CancellationToken cancellationToken)
        => RunAsync(
            ModAction.Note,
            async () =>
            {
                TextQuestion question = new(Strings.ModNoteTitle, Text.Format(Strings.ModNoteMessage, Name), Strings.ModNoteHint, Strings.ModNoteSave)
                {
                    Text = Note ?? string.Empty,
                    IsMultiline = true,
                    MaxLength = LibraryMod.MaxNoteLength,
                };
                if (await _services.Dialogs.AskTextAsync(question, cancellationToken) is not { } note)
                {
                    return;
                }

                try
                {
                    await _services.Library.SetNoteAsync(Mod, note, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
            },
            cancellationToken);

    /// <summary>
    /// Offers again the updates the user skipped.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSkipped))]
    private Task OfferSkippedAsync(CancellationToken cancellationToken)
        => RunAsync(
            ModAction.OfferSkipped,
            async () =>
            {
                foreach (ModFileRef skipped in _mod.SkippedUpdates)
                {
                    try
                    {
                        await _services.Library.SkipUpdateAsync(skipped, false, cancellationToken);
                    }
                    catch (KeyNotFoundException)
                    {
                        // It left the library meanwhile.
                        return;
                    }
                }
            },
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanWatch))]
    private Task WatchAsync(CancellationToken cancellationToken) => SetWatchedAsync(ModAction.Watch, true, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsWatched))]
    private Task UnwatchAsync(CancellationToken cancellationToken) => SetWatchedAsync(ModAction.Unwatch, false, cancellationToken);

    /// <summary>
    /// Takes the mod out of the library once the user confirms. Its files stay where they are. The screen shows the change
    /// once the library makes it.
    /// </summary>
    [RelayCommand]
    private Task RemoveAsync(CancellationToken cancellationToken)
        => RunAsync(
            ModAction.Remove,
            async () =>
            {
                string message = Text.Format(Strings.RemoveModMessage, Name);
                if (!await _services.Dialogs.ConfirmAsync(Strings.RemoveModTitle, message, Strings.RemoveModConfirm, cancellationToken))
                {
                    return;
                }

                try
                {
                    await _services.Library.ForgetAsync(Mod, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
                catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
                {
                    // Such as a download of one of its files that isn't finished, or settings that can't be read.
                    await _services.Dialogs.ShowMessageAsync(Strings.RemoveModFailedTitle, exception.Message, cancellationToken);
                }
            },
            cancellationToken);

    /// <summary>
    /// Rescans the mod's folders, then says what it found. The mod is found by its page in the library, without asking
    /// its site, which is asked only when there are files to match.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPage))]
    private Task RescanAsync(CancellationToken cancellationToken)
        => RunAsync(ModAction.Rescan, () => Rescans.RunAsync(_services.Rescan, _services.Library, _services.Dialogs, _mod.PageUrl, cancellationToken), cancellationToken);

    private bool CanWatch() => !IsWatched;

    private bool CanPin() => !IsPinned;

    /// <summary>
    /// Pins or unpins the mod. The screen shows the change once the library makes it.
    /// </summary>
    private Task SetPinnedAsync(ModAction action, bool pinned, CancellationToken cancellationToken)
        => RunAsync(
            action,
            async () =>
            {
                try
                {
                    await _services.Library.SetPinnedAsync(Mod, pinned, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
            },
            cancellationToken);

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

    /// <summary>
    /// Whether it's pinned, such as "Pinned at 2.7"; its updates offered: "Update to" the new version when one replaces a
    /// file, a new optional file, or how many; how many it skipped; then the files missing from disk; then whether it's
    /// watched only, or not watched.
    /// </summary>
    private void ShowStatuses()
    {
        List<ModStatus> statuses = [];
        if (_mod.IsPinned)
        {
            string text = VersionText == Strings.NoValue ? Strings.ModStatusPinned : Text.Format(Strings.ModStatusPinnedAt, VersionText);
            statuses.Add(new ModStatus(text, ModStatusKind.Note));
        }

        IReadOnlyList<LibraryUpdate> offered = _mod.OfferedUpdates;
        if (offered is [LibraryUpdate update])
        {
            string text = update.Replaces is not null
                ? Text.Format(Strings.ModStatusUpdateTo, update.VersionLabel ?? update.Name)
                : update.Category == ModFileCategory.Optional ? Strings.ModPageNewOptionalFile : Strings.ModPageUpdate;
            statuses.Add(new ModStatus(text, ModStatusKind.Update));
        }
        else if (offered.Count > 1)
        {
            statuses.Add(new ModStatus(Text.Format(Strings.ModStatusUpdates, offered.Count), ModStatusKind.Update));
        }

        if (!_mod.IsPinned && _mod.SkippedUpdates.Count > 0)
        {
            statuses.Add(new ModStatus(Counts.Of(_mod.SkippedUpdates.Count, Strings.ModStatusOneSkipped, Strings.ModStatusSkipped), ModStatusKind.Note));
        }

        if (MissingCount > 0)
        {
            statuses.Add(new ModStatus(Counts.Of(MissingCount, Strings.ModStatusOneMissing, Strings.ModStatusMissing), ModStatusKind.Problem));
        }

        if (!_mod.IsWatched)
        {
            statuses.Add(new ModStatus(Strings.ModStatusNotWatched, ModStatusKind.Note));
        }
        else if (_mod.Files.Count == 0)
        {
            statuses.Add(new ModStatus(Strings.ModStatusWatchedOnly, ModStatusKind.Note));
        }

        Statuses = statuses;
    }

    private Task RunAsync(ModAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, Mod.Id, Mod.Game.Key), cancellationToken);
}
