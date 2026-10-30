using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Catalog;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A mod opened on the Browse screen, as its site describes it: its details, its files by section, and its changelog,
/// with watching it in the library and tracking it on its site.
/// </summary>
public sealed partial class ModPageViewModel : ObservableObject
{
    // The sections shown at once, in the order the sites show them; the others only when asked.
    private static readonly ModFileCategory[] CurrentSections =
        [ModFileCategory.Main, ModFileCategory.Update, ModFileCategory.Optional, ModFileCategory.Miscellaneous];

    private static readonly ModFileCategory[] OtherSections =
        [ModFileCategory.OldVersion, ModFileCategory.Archived, ModFileCategory.Unknown];

    private readonly BrowseServices _services;
    private readonly ModInfo _info;
    private readonly List<(ModFileCategory Category, ModFileSectionViewModel Section)> _sections = [];
    private readonly List<ModFileViewModel> _files = [];
    private readonly ModFileRef? _linkedFile;

    internal ModPageViewModel(ModPage page, LibraryMod? inLibrary, BrowseServices services)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _info = page.Mod;
        _linkedFile = page.LinkedFile;
        Mod = page.Mod.Mod;
        Name = page.Mod.Name;
        GameName = page.Game.Name;
        ModInfoText text = ModInfoText.Of(page.Mod, services.TimeProvider);
        AuthorText = text.Author;
        Summary = text.Summary;
        VersionText = text.Version;
        UpdatedText = text.Updated;
        DownloadsText = text.Downloads;
        EndorsementsText = text.Endorsements;
        HasAdultContent = page.Mod.HasAdultContent;
        IsUnavailable = !page.Mod.IsAvailable;
        HasPage = page.Mod.PageUrl is not null;
        Changelog = page.Changelogs;
        CanTrack = services.Tracked.CanTrack(Mod.Game.Source);
        string site = services.Sources.SiteName(Mod.Game.Source);
        TrackText = Text.Format(Strings.BrowseTrack, site);
        UntrackText = Text.Format(Strings.BrowseUntrack, site);

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
        ShowLibrary(inLibrary);
        ShowSections();
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
    }

    public ModRef Mod { get; }

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

    public bool HasAdultContent { get; }

    /// <summary>
    /// Whether the mod is hidden or removed on its site, so its files can't be downloaded.
    /// </summary>
    public bool IsUnavailable { get; }

    public bool HasPage { get; }

    /// <summary>
    /// Whether the site lists any files for the mod.
    /// </summary>
    public bool HasFiles { get; }

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
    /// Each version's changes, in the order the site gives them.
    /// </summary>
    public IReadOnlyList<ModChangelog> Changelog { get; }

    public bool HasChangelog => Changelog.Count > 0;

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
    /// Shows what the library has of the mod now; null when nothing. Runs on the UI thread.
    /// </summary>
    internal void ShowLibrary(LibraryMod? inLibrary)
    {
        IsDownloaded = inLibrary?.Files.Count > 0;
        IsWatched = inLibrary?.IsWatched == true;
        HashSet<ModFileRef> kept = [.. inLibrary?.Files.Select(file => file.File) ?? []];
        foreach (ModFileViewModel file in _files)
        {
            file.ShowLibrary(kept.Contains(file.File));
        }
    }

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
            catch (Exception exception) when (exception is AuthenticationFailedException or HttpRequestException)
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
            async () =>
            {
                if (_info.PageUrl is { } page && !await _services.Browser.TryOpenAsync(page, cancellationToken))
                {
                    await _services.Dialogs.ShowMessageAsync(Strings.OpenPageFailedTitle, Text.Format(Strings.PageOpenFailed, page.AbsoluteUri), cancellationToken);
                }
            },
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
                catch (AuthenticationFailedException)
                {
                    Tell(Text.Format(Strings.BrowseAccountNeeded, _services.Sources.SiteName(Mod.Game.Source)), isProblem: true);
                    return;
                }
                catch (HttpRequestException exception)
                {
                    Tell(Text.Format(Strings.BrowseWatchFailed, _services.Sources.SiteName(Mod.Game.Source), exception.Message), isProblem: true);
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
        return RunAsync(
            track ? PageAction.Track : PageAction.Untrack,
            async () =>
            {
                try
                {
                    await _services.Tracked.SetAsync(Mod, track, cancellationToken);
                }
                catch (AuthenticationFailedException)
                {
                    Tell(Text.Format(Strings.BrowseAccountNeeded, site), isProblem: true);
                    return;
                }
                catch (HttpRequestException exception)
                {
                    Tell(Text.Format(Strings.BrowseTrackFailed, site, exception.Message), isProblem: true);
                    return;
                }

                IsTracked = track;
                Tell(Text.Format(track ? Strings.BrowseTrackedNote : Strings.BrowseUntrackedNote, site), isProblem: false);
            },
            cancellationToken);
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

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    private Task RunAsync(PageAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, Mod.Id, Mod.Game.Key), cancellationToken);
}
