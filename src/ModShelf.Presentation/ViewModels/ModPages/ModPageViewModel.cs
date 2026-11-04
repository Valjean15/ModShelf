using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// A mod's page, opened from the Library or Browse screen: its picture and details as its site describes them, the
/// library's files of it, its files on the site by section, its latest changes and what it needs; with watching it,
/// tracking it on its site, rescanning its folders and removing it from the library. ModShelf only downloads what it
/// needs; it never installs anything.
/// </summary>
public sealed partial class ModPageViewModel : ObservableObject
{
    /// <summary>
    /// How many of the changelog's entries show before the user asks for them all.
    /// </summary>
    internal const int LatestChanges = 3;

    // The sections shown at once, in the order the sites show them; the others only when asked.
    private static readonly ModFileCategory[] CurrentSections =
        [ModFileCategory.Main, ModFileCategory.Update, ModFileCategory.Optional, ModFileCategory.Miscellaneous];

    private static readonly ModFileCategory[] OtherSections =
        [ModFileCategory.OldVersion, ModFileCategory.Archived, ModFileCategory.Unknown];

    private readonly ModPageServices _services;
    private readonly ModInfo _info;
    private readonly List<(ModFileCategory Category, ModFileSectionViewModel Section)> _sections = [];
    private readonly List<ModFileViewModel> _files = [];
    private readonly ModFileRef? _linkedFile;
    private readonly Func<ModRef, LibraryMod?> _library;
    private LibraryMod? _inLibrary;
    private LibraryRoots? _roots;

    /// <param name="library">What the library has of a mod now; null when nothing.</param>
    /// <param name="previews">The screen's previews, which say whether the page shows the mod's picture, and how
    /// large.</param>
    /// <param name="open">Opens another mod's page on the screen, such as one the mod needs.</param>
    /// <param name="stopping">Cancelled when the app closes.</param>
    internal ModPageViewModel(
        ModPage page,
        Func<ModRef, LibraryMod?> library,
        ModPageServices services,
        PreviewsViewModel previews,
        Func<ModRef, CancellationToken, Task> open,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(open);

        _services = services;
        _info = page.Mod;
        _linkedFile = page.LinkedFile;
        _library = library;
        Previews = previews;
        Picture = new ModPictureViewModel(services.Pictures);
        if (previews.ShowsPictures)
        {
            Picture.Show(page.Mod, stopping);
        }

        Mod = page.Mod.Mod;
        Name = page.Mod.Name;
        GameName = page.Game.Name;
        SiteWords words = services.Sources.Words(Mod.Game.Source);
        ModInfoText text = ModInfoText.Of(page.Mod, words, services.TimeProvider);
        AuthorText = text.Author;
        Summary = text.Summary;
        VersionText = text.Version;
        UpdatedText = text.Updated;
        DownloadsText = text.Downloads;
        EndorsementsText = text.Endorsements;
        HasAdultContent = page.Mod.HasAdultContent;
        IsUnavailable = !page.Mod.IsAvailable;
        HasPage = page.Mod.PageUrl is not null;
        CategoryText = page.Mod.Category ?? string.Empty;
        Changelog = NewestFirst(page.Changelogs, page.Mod.Version);
        CanTrack = services.Tracked.CanTrack(Mod.Game.Source) && words.CanFollow;
        string site = services.Sources.SiteName(Mod.Game.Source);
        TrackText = words.Follow is { } follow ? Text.Format(Strings.BrowseFollow, Text.Capitalized(follow), site) : string.Empty;
        UntrackText = words.Unfollow is { } unfollow ? Text.Format(Strings.BrowseUnfollow, Text.Capitalized(unfollow), site) : string.Empty;
        OpenPageText = Text.Format(Strings.JobOpenPage, site);
        SiteFilesHeading = Text.Format(Strings.ModPageSiteFiles, site);
        OfflineText = Text.Format(Strings.ModPageShownOnline, site);
        Requirements = [.. page.Requirements.Select(requirement => new RequirementViewModel(requirement, services, open))];

        foreach (ModFileCategory category in CurrentSections.Concat(OtherSections))
        {
            ModFileViewModel[] files =
            [
                .. page.Files
                    .Where(file => file.Category == category)
                    .OrderByDescending(file => file.UploadedAt)
                    .ThenByDescending(file => file.Reference.Id, StringComparer.Ordinal)
                    .Select(file => new ModFileViewModel(file, file.Reference == page.LinkedFile, page.Mod.IsAvailable, services)),
            ];
            if (files.Length > 0)
            {
                _sections.Add((category, new ModFileSectionViewModel(SectionTitle(category), files)));
                _files.AddRange(files);
            }
        }

        HasFiles = _files.Count > 0;
        ShowLibrary();
        ShowSections();
        ShowChanges();
    }

    /// <summary>
    /// What the user asked of a mod, as the log names it.
    /// </summary>
    private enum PageAction
    {
        OpenPage,
        Watch,
        Unwatch,
        Track,
        Untrack,
        OpenFolder,
        Rescan,
        Remove,
        CopySha256,
        DeleteFile,
    }

    public ModRef Mod { get; }

    /// <summary>
    /// The screen's previews: none shows no picture, thumbnails a small one beside the name, cards a large one above.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// The mod's picture, while the screen shows pictures.
    /// </summary>
    public ModPictureViewModel Picture { get; }

    public string Name { get; }

    /// <summary>
    /// Such as "by schlangster"; empty when the site names no author.
    /// </summary>
    public string AuthorText { get; }

    public string GameName { get; }

    /// <summary>
    /// A line or two about the mod; empty when the site gives none.
    /// </summary>
    public string Summary { get; }

    /// <inheritdoc cref="BrowseModViewModel.VersionText"/>
    public string VersionText { get; }

    /// <inheritdoc cref="BrowseModViewModel.UpdatedText"/>
    public string UpdatedText { get; }

    /// <inheritdoc cref="BrowseModViewModel.DownloadsText"/>
    public string DownloadsText { get; }

    /// <inheritdoc cref="BrowseModViewModel.EndorsementsText"/>
    public string EndorsementsText { get; }

    /// <summary>
    /// The mod's category on its site, such as "Gameplay"; empty when it doesn't say.
    /// </summary>
    public string CategoryText { get; }

    public bool HasCategory => CategoryText.Length > 0;

    public bool HasAdultContent { get; }

    /// <summary>
    /// Whether the mod is hidden or removed on its site, so its files can't be downloaded.
    /// </summary>
    public bool IsUnavailable { get; }

    public bool HasPage { get; }

    /// <summary>
    /// Such as "Open on Nexus Mods".
    /// </summary>
    public string OpenPageText { get; }

    /// <summary>
    /// Such as "Files on Nexus Mods".
    /// </summary>
    public string SiteFilesHeading { get; }

    /// <summary>
    /// Whether the site lists any files for the mod.
    /// </summary>
    public bool HasFiles { get; }

    /// <summary>
    /// Whether the page shows the mod's saved details because its site couldn't give it, so its files, changelog and
    /// requirements aren't known.
    /// </summary>
    public bool IsOffline { get; internal init; }

    /// <summary>
    /// What's said in place of the files and the changelog while <see cref="IsOffline"/>, such as "Shown once Nexus Mods
    /// gives the mod."
    /// </summary>
    public string OfflineText { get; }

    /// <summary>
    /// Whether to say the site lists no files: not while it couldn't be asked.
    /// </summary>
    public bool ShowsNoFiles => !HasFiles && !IsOffline;

    /// <summary>
    /// The sections of files shown: the current ones always; old versions, archived files and files in no section when
    /// asked, or when the link the user gave is to one of them.
    /// </summary>
    public ObservableCollection<ModFileSectionViewModel> Sections { get; } = [];

    /// <summary>
    /// Whether some files are in sections shown only when asked.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMoreFiles { get; private set; }

    /// <summary>
    /// Whether the sections shown only when asked are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool AreMoreFilesShown { get; private set; }

    /// <summary>
    /// Such as "Show 3 more files: old versions, archived, or in no section", or what hides them again.
    /// </summary>
    [ObservableProperty]
    public partial string MoreFilesText { get; private set; } = string.Empty;

    /// <summary>
    /// Each version's changes, the newest first as far as can be told: the site's order, turned round when it lists the
    /// mod's current version last.
    /// </summary>
    public IReadOnlyList<ModChangelog> Changelog { get; }

    public bool HasChangelog => Changelog.Count > 0;

    /// <summary>
    /// Whether to say the mod has no changelog: not while its site couldn't be asked.
    /// </summary>
    public bool ShowsNoChangelog => !HasChangelog && !IsOffline;

    /// <summary>
    /// The changelog's entries shown: the latest few, or all of them when asked.
    /// </summary>
    public ObservableCollection<ModChangelog> ShownChanges { get; } = [];

    /// <summary>
    /// Whether the changelog has more entries than the latest few.
    /// </summary>
    public bool HasMoreChanges => Changelog.Count > LatestChanges;

    [ObservableProperty]
    public partial bool AreAllChangesShown { get; private set; }

    /// <summary>
    /// Such as "All 9 entries", or what shows only the latest again.
    /// </summary>
    [ObservableProperty]
    public partial string MoreChangesText { get; private set; } = string.Empty;

    /// <summary>
    /// What the mod needs, as its site lists it; empty when it lists nothing, or the site can't say.
    /// </summary>
    public IReadOnlyList<RequirementViewModel> Requirements { get; }

    public bool HasRequirements => Requirements.Count > 0;

    /// <summary>
    /// The library's files of the mod, the latest downloaded first: the current ones, and the archived ones when asked.
    /// </summary>
    public ObservableCollection<OwnFileViewModel> OwnFiles { get; } = [];

    /// <summary>
    /// Whether the library has files of the mod.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial bool HasOwnFiles { get; private set; }

    /// <summary>
    /// The mod's folder in the library, where its latest file is, such as "nexus/stardewvalley/SMAPI (2400)"; empty
    /// without files.
    /// </summary>
    [ObservableProperty]
    public partial string OwnFolderText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether some of the library's files of the mod are old versions in an <c>_archive</c> folder.
    /// </summary>
    [ObservableProperty]
    public partial bool HasArchived { get; private set; }

    [ObservableProperty]
    public partial bool AreArchivedShown { get; private set; }

    /// <summary>
    /// Such as "Show 1 archived version", or what hides them again.
    /// </summary>
    [ObservableProperty]
    public partial string ArchivedText { get; private set; } = string.Empty;

    /// <summary>
    /// The mod's ID, category, and when it came into the library and was last checked for updates.
    /// </summary>
    public ObservableCollection<ModFact> Facts { get; } = [];

    /// <summary>
    /// Whether the library has the mod, watched or with files.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand), nameof(RemoveCommand))]
    public partial bool IsInLibrary { get; private set; }

    /// <summary>
    /// Whether the library has files of it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloaded { get; private set; }

    /// <summary>
    /// Whether the library watches it for updates.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWatched { get; private set; }

    /// <summary>
    /// Whether the user pinned it at the version the library has, so its updates aren't offered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKept))]
    public partial bool IsPinned { get; private set; }

    /// <summary>
    /// Such as "Pinned at 2.7: its updates aren't offered."; empty when it isn't pinned.
    /// </summary>
    [ObservableProperty]
    public partial string PinnedText { get; private set; } = string.Empty;

    /// <summary>
    /// The user's note of it in the library; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKept))]
    public partial string? UserNote { get; private set; }

    /// <summary>
    /// Whether it's pinned or has a note, which show above its files.
    /// </summary>
    public bool HasKept => IsPinned || UserNote is not null;

    /// <summary>
    /// Whether its site keeps a list of tracked mods.
    /// </summary>
    public bool CanTrack { get; }

    /// <summary>
    /// Whether the linked account tracks it on its site, as far as the screen knows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTracked { get; private set; }

    /// <summary>
    /// Such as "Track on Nexus Mods".
    /// </summary>
    public string TrackText { get; }

    /// <summary>
    /// Such as "Untrack on Nexus Mods".
    /// </summary>
    public string UntrackText { get; }

    /// <summary>
    /// What became of watching or tracking it, or why it didn't work; null for nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Its files, in the order shown.
    /// </summary>
    internal IReadOnlyList<ModFileViewModel> Files => _files;

    /// <summary>
    /// Reads where the library is, so the library's files of the mod say whether they're still on disk. Settings that
    /// can't be read leave that unsaid.
    /// </summary>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _roots = await _services.Settings.GetLibraryRootsAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return;
        }

        ShowOwnFiles();
    }

    /// <summary>
    /// Shows what the library has now of the mod, and of the mods it needs. Runs on the UI thread.
    /// </summary>
    internal void ShowLibrary() => ShowLibrary(_library(Mod));

    /// <summary>
    /// Shows whether the account tracks the mod, reading its site's list the first time any mod of the site asks. When
    /// the list can't be read, such as without a linked account, it's logged, and tracking the mod is still offered.
    /// </summary>
    internal async Task ShowTrackedAsync(CancellationToken cancellationToken)
    {
        if (!CanTrack)
        {
            return;
        }

        if (_services.Tracked.IsTracked(Mod) is not { } tracked)
        {
            try
            {
                await _services.Tracked.ReadAsync(Mod.Game.Source, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
            {
                // Expected without a linked account, or offline: no stack trace for it.
                LogTrackedUnknown(_services.Logger, Mod.Game.Source.Value, exception.Message);
                return;
            }
            catch (Exception exception)
            {
                LogTrackedFailed(_services.Logger, exception, Mod.Game.Source.Value);
                return;
            }

            tracked = _services.Tracked.IsTracked(Mod) == true;
        }

        IsTracked = tracked;
    }

    /// <summary>
    /// The changelog, the newest entry first: in the site's order, unless the site lists the mod's current version
    /// last and not first. Versions are only matched, never compared.
    /// </summary>
    private static IReadOnlyList<ModChangelog> NewestFirst(IReadOnlyList<ModChangelog> changelog, string? currentVersion)
    {
        bool Current(ModChangelog entry) => currentVersion is not null && string.Equals(entry.Version, currentVersion.Trim(), StringComparison.Ordinal);

        return changelog.Count > 1 && Current(changelog[^1]) && !Current(changelog[0]) ? [.. changelog.Reverse()] : changelog;
    }

    /// <summary>
    /// What a file on the site is to the library: an update of one of its files, such as "Update for your 1.3", a new
    /// optional file, or another update; null for none, or for one not offered, of a pinned mod or skipped.
    /// </summary>
    private static string? UpdateNote(LibraryMod? inLibrary, ModFileRef file)
    {
        if (inLibrary?.OfferedUpdates.FirstOrDefault(update => update.File == file) is not { } update)
        {
            return null;
        }

        if (update.Replaces is { } replaced && inLibrary.Files.FirstOrDefault(kept => kept.File == replaced) is { } yours)
        {
            return Text.Format(Strings.ModPageUpdateFor, yours.VersionLabel ?? yours.Name);
        }

        return update.Replaces is null && update.Category == ModFileCategory.Optional ? Strings.ModPageNewOptionalFile : Strings.ModPageUpdate;
    }

    private static string SectionTitle(ModFileCategory category) => category switch
    {
        ModFileCategory.Main => Strings.FilesMain,
        ModFileCategory.Update => Strings.FilesUpdate,
        ModFileCategory.Optional => Strings.FilesOptional,
        ModFileCategory.Miscellaneous => Strings.FilesMiscellaneous,
        ModFileCategory.OldVersion => Strings.FilesOldVersion,
        ModFileCategory.Archived => Strings.FilesArchived,
        _ => Strings.FilesUnknown,
    };

    /// <summary>
    /// Opens the mod's page on its site. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPage))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => RunAsync(
            PageAction.OpenPage,
            () => _info.PageUrl is { } page
                ? Pages.OpenAsync(page, _services.Browser, _services.Dialogs, Strings.OpenPageByHand, cancellationToken)
                : Task.CompletedTask,
            cancellationToken);

    /// <summary>
    /// Watches the mod for updates, adding it to the library with its details from its site, or stops watching it. A mod
    /// unwatched without files leaves the library.
    /// </summary>
    [RelayCommand]
    private Task ToggleWatchAsync(CancellationToken cancellationToken)
    {
        bool watch = !IsWatched;
        return RunAsync(
            watch ? PageAction.Watch : PageAction.Unwatch,
            async () =>
            {
                LibraryMod? now;
                try
                {
                    now = watch
                        ? await _services.Library.WatchAsync(Mod, cancellationToken)
                        : (await _services.Library.SetWatchedAsync(Mod, watched: false, cancellationToken)).After;
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile, so it isn't watched.
                    now = null;
                }
                catch (Exception exception)
                    when (SiteProblems.Describe(exception, _services.Sources.SiteName(Mod.Game.Source), Strings.BrowseWatchFailed) is { } problem)
                {
                    Tell(problem, isProblem: true);
                    return;
                }

                ShowLibrary(now);
                Tell(watch ? Strings.BrowseWatchedNote : Strings.BrowseUnwatchedNote, isProblem: false);
            },
            cancellationToken);
    }

    /// <summary>
    /// Tracks the mod on its site with the linked account, or stops tracking it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTrack))]
    private Task ToggleTrackAsync(CancellationToken cancellationToken)
    {
        bool track = !IsTracked;
        string site = _services.Sources.SiteName(Mod.Game.Source);
        string followed = _services.Sources.Words(Mod.Game.Source).Followed ?? string.Empty;
        return RunAsync(
            track ? PageAction.Track : PageAction.Untrack,
            async () =>
            {
                try
                {
                    await _services.Tracked.SetAsync(Mod, track, cancellationToken);
                }
                catch (Exception exception) when (SiteProblems.Describe(exception, site, SiteProblems.InWords(Strings.BrowseFollowFailed, followed)) is { } problem)
                {
                    Tell(problem, isProblem: true);
                    return;
                }

                IsTracked = track;
                Tell(
                    track
                        ? Text.Format(Strings.BrowseFollowedNote, Text.Capitalized(followed), site)
                        : Text.Format(Strings.BrowseUnfollowedNote, followed, site),
                    isProblem: false);
            },
            cancellationToken);
    }

    /// <summary>
    /// Opens the mod's folder in the library: the folder of the file downloaded last.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasOwnFiles))]
    private Task OpenFolderAsync(CancellationToken cancellationToken)
        => RunAsync(
            PageAction.OpenFolder,
            async () =>
            {
                if (_inLibrary?.Files.MaxBy(file => file.DownloadedAt) is not { } latest)
                {
                    return;
                }

                LibraryRoots roots;
                try
                {
                    roots = await _services.Settings.GetLibraryRootsAsync(cancellationToken);
                }
                catch (InvalidDataException exception)
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.OpenFolderFailedTitle, exception.Message, cancellationToken);
                    return;
                }

                string folder = Path.GetDirectoryName(roots.Resolve(latest))!;
                if (!await _services.FolderOpener.TryOpenAsync(folder, cancellationToken))
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.OpenFolderFailedTitle, Text.Format(Strings.OpenFolderFailed, folder), cancellationToken);
                }
            },
            cancellationToken);

    private bool CanRescan() => IsInLibrary && (_inLibrary?.PageUrl ?? _info.PageUrl) is not null;

    /// <summary>
    /// Rescans the mod's folders, then says what it found. The page shows the library's changes as it makes them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanAsync(CancellationToken cancellationToken)
        => RunAsync(
            PageAction.Rescan,
            () => Rescans.RunAsync(_services.Rescan, _services.Library, _services.Dialogs, _inLibrary?.PageUrl ?? _info.PageUrl, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Takes the mod out of the library once the user confirms. Its files stay where they are.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsInLibrary))]
    private Task RemoveAsync(CancellationToken cancellationToken)
        => RunAsync(
            PageAction.Remove,
            async () =>
            {
                string message = Text.Format(Strings.RemoveModMessage, _inLibrary?.Name ?? Name);
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
    /// Shows all the changelog's entries, or only the latest again.
    /// </summary>
    [RelayCommand]
    private void ToggleChanges()
    {
        AreAllChangesShown = !AreAllChangesShown;
        ShowChanges();
    }

    /// <summary>
    /// Shows the archived versions among the library's files of the mod, or hides them again.
    /// </summary>
    [RelayCommand]
    private void ToggleArchived()
    {
        AreArchivedShown = !AreArchivedShown;
        ShowOwnFiles();
    }

    /// <summary>
    /// Shows or hides the sections shown only when asked.
    /// </summary>
    [RelayCommand]
    private void ToggleMoreFiles()
    {
        AreMoreFilesShown = !AreMoreFilesShown;
        ShowSections();
    }

    /// <summary>
    /// Shows the current sections, and the others when asked or when they hold the linked file.
    /// </summary>
    private void ShowSections()
    {
        bool Forced(ModFileSectionViewModel section) => section.Files.Any(file => file.File == _linkedFile);

        int more = _sections
            .Where(entry => OtherSections.Contains(entry.Category) && !Forced(entry.Section))
            .Sum(entry => entry.Section.Files.Count);
        Sections.SyncWith(
        [
            .. _sections
                .Where(entry => CurrentSections.Contains(entry.Category) || AreMoreFilesShown || Forced(entry.Section))
                .Select(entry => entry.Section),
        ]);
        HasMoreFiles = more > 0;
        MoreFilesText = AreMoreFilesShown
            ? Strings.BrowseHideMoreFiles
            : Counts.Of(more, Strings.BrowseShowOneMoreFile, Strings.BrowseShowMoreFiles);
    }

    /// <summary>
    /// Shows what the library has of the mod, as given, and what it has now of the mods it needs.
    /// </summary>
    private void ShowLibrary(LibraryMod? inLibrary)
    {
        _inLibrary = inLibrary;
        IsDownloaded = inLibrary?.Files.Count > 0;
        IsWatched = inLibrary?.IsWatched == true;
        IsInLibrary = inLibrary is not null;
        IsPinned = inLibrary?.IsPinned == true;
        UserNote = inLibrary?.Note;
        string? version = inLibrary?.Files
            .Where(file => !OwnFileViewModel.IsInArchive(file.RelativePath))
            .OrderByDescending(file => file.Category == ModFileCategory.Main)
            .ThenByDescending(file => file.DownloadedAt)
            .FirstOrDefault()?.VersionLabel;
        PinnedText = !IsPinned ? string.Empty
            : version is null ? Strings.ModPagePinned
            : Text.Format(Strings.ModPagePinnedAt, version);
        HashSet<ModFileRef> kept = [.. inLibrary?.Files.Select(file => file.File) ?? []];
        foreach (ModFileViewModel file in _files)
        {
            file.ShowLibrary(kept.Contains(file.File), UpdateNote(inLibrary, file.File));
        }

        foreach (RequirementViewModel requirement in Requirements)
        {
            requirement.ShowLibrary(requirement.Mod is { } needed && _library(needed) is { Files.Count: > 0 });
        }

        ShowOwnFiles();
        ShowFacts();
    }

    private void ShowChanges()
    {
        ShownChanges.SyncWith([.. AreAllChangesShown ? Changelog : Changelog.Take(LatestChanges)]);
        MoreChangesText = AreAllChangesShown ? Strings.ModPageLatestChanges : Text.Format(Strings.ModPageAllChanges, Changelog.Count);
    }

    /// <summary>
    /// Shows the library's files of the mod, the latest downloaded first, each saying whether it's on disk once the
    /// library's folder is known; archived versions when asked.
    /// </summary>
    private void ShowOwnFiles()
    {
        OwnFileViewModel[] files =
        [
            .. (_inLibrary?.Files ?? [])
                .OrderByDescending(file => file.DownloadedAt)
                .Select(file => new OwnFileViewModel(
                    file,
                    _roots is { } roots && !_services.Storage.FileExists(roots.Resolve(file)),
                    _services.TimeProvider,
                    CopyAsync,
                    DeleteFileAsync)),
        ];
        int archived = files.Count(file => file.IsArchived);
        OwnFiles.Clear();
        foreach (OwnFileViewModel file in files.Where(file => !file.IsArchived || AreArchivedShown))
        {
            OwnFiles.Add(file);
        }

        HasOwnFiles = files.Length > 0;
        HasArchived = archived > 0;
        ArchivedText = AreArchivedShown ? Strings.ModPageHideArchived : Counts.Of(archived, Strings.ModPageShowOneArchived, Strings.ModPageShowArchived);
        string? latest = _inLibrary?.Files.Where(file => !OwnFileViewModel.IsInArchive(file.RelativePath)).MaxBy(file => file.DownloadedAt)?.RelativePath;
        int folderEnd = latest?.LastIndexOf(LibraryPaths.Separator) ?? -1;
        OwnFolderText = folderEnd > 0 ? latest![..folderEnd] : string.Empty;
    }

    private void ShowFacts()
    {
        List<ModFact> facts = [new(Strings.ModPageModId, Mod.Id)];
        if (HasCategory)
        {
            facts.Add(new ModFact(Strings.ModPageCategory, CategoryText));
        }

        if (_inLibrary is { } inLibrary)
        {
            facts.Add(new ModFact(Strings.ModPageInLibrarySince, Dates.Day(inLibrary.AddedAt, _services.TimeProvider)));
            facts.Add(new ModFact(
                Strings.ModPageLastChecked,
                inLibrary.CheckedAt is { } checkedAt ? Dates.DayAndTime(checkedAt, _services.TimeProvider) : Strings.ModPageNotChecked));
        }

        Facts.SyncWith(facts);
    }

    /// <summary>
    /// Deletes one of the library's files of the mod from disk, for good, and from the library, once the user confirms.
    /// The page shows the change once the library makes it.
    /// </summary>
    private Task DeleteFileAsync(OwnFileViewModel file, CancellationToken cancellationToken)
        => RunAsync(
            PageAction.DeleteFile,
            async () =>
            {
                string message = Text.Format(Strings.DeleteFileMessage, file.FileName, file.SizeText);
                if (!await _services.Dialogs.ConfirmAsync(Strings.DeleteFileTitle, message, Strings.DeleteFileConfirm, cancellationToken))
                {
                    return;
                }

                try
                {
                    await _services.Library.DeleteFileAsync(file.File, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    string reason = exception is InvalidDataException ? exception.Message : Text.Format(Strings.DeleteFileFailed, file.FileName, exception.Message);
                    await _services.Dialogs.ShowMessageAsync(Strings.DeleteFileFailedTitle, reason, cancellationToken);
                }
            },
            cancellationToken);

    private Task CopyAsync(string text, CancellationToken cancellationToken)
        => RunAsync(PageAction.CopySha256, () => _services.Clipboard.SetTextAsync(text, cancellationToken), cancellationToken);

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    private Task RunAsync(PageAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, Mod.Id, Mod.Game.Key), cancellationToken);
}
