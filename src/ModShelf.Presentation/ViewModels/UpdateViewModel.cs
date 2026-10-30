using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// One update on the Updates screen: a newer file of a watched mod, with downloading it into the library and reading its
/// changes.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdatesServices _services;
    private LibraryUpdate _update;

    internal UpdateViewModel(LibraryUpdate update, LibraryMod mod, UpdatesServices services)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(services);

        _update = update;
        _services = services;
        File = update.File;
        Show(update, mod);
    }

    /// <summary>
    /// What the user asked of an update, as the log names it.
    /// </summary>
    private enum UpdateAction
    {
        Download,
        Changes,
    }

    public ModFileRef File { get; }

    [ObservableProperty]
    public partial string ModName { get; private set; } = string.Empty;

    /// <summary>
    /// The file's name on its site, such as "SkyUI 5.3".
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Version 5.3"; empty when the site gives none.
    /// </summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Uploaded 10/27/2026", in the user's time zone and culture.
    /// </summary>
    [ObservableProperty]
    public partial string UploadedText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Replaces SkyUI_5_2_SE.7z"; empty when it replaces no file the library has.
    /// </summary>
    [ObservableProperty]
    public partial string ReplacesText { get; private set; } = string.Empty;

    /// <summary>
    /// When the update was uploaded, which orders a mod's updates.
    /// </summary>
    public DateTimeOffset UploadedAt => _update.UploadedAt;

    /// <summary>
    /// Whether the site gives a link to the file, which downloading needs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool CanDownload { get; private set; }

    /// <summary>
    /// What became of downloading it, such as where it downloads to, or why it couldn't; null for nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Whether its changes show.
    /// </summary>
    [ObservableProperty]
    public partial bool AreChangesShown { get; private set; }

    /// <summary>
    /// Its version's entry in its mod's changelog, a line per change.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Changes { get; private set; } = [];

    /// <summary>
    /// What to say instead of the changes, or above them: that they're being read, that the changelog says nothing, or
    /// why it couldn't be read. Empty with changes to show.
    /// </summary>
    [ObservableProperty]
    public partial string ChangesNote { get; private set; } = string.Empty;

    /// <summary>
    /// Shows the update as the library has it now, with its mod's name and the file it replaces. Runs on the UI thread.
    /// </summary>
    internal void Show(LibraryUpdate update, LibraryMod mod)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mod);

        _update = update;
        ModName = mod.Name;
        Name = update.Name;
        FileName = update.FileName;
        VersionText = update.VersionLabel is { } version ? Text.Format(Strings.LibraryFileVersion, version) : string.Empty;
        DateTimeOffset uploaded = TimeZoneInfo.ConvertTime(update.UploadedAt, _services.TimeProvider.LocalTimeZone);
        UploadedText = Text.Format(Strings.UpdateUploaded, uploaded.ToString("d", CultureInfo.CurrentCulture));
        ReplacesText = mod.Files.FirstOrDefault(file => file.File == update.Replaces) is { } replaced
            ? Text.Format(Strings.UpdateReplaces, replaced.FileName)
            : string.Empty;
        CanDownload = update.PageUrl is not null;
    }

    /// <summary>
    /// Downloads the update into the library, beside the file it replaces, as Download on the Downloads screen does. Once
    /// the file is recorded, the update is no longer one, and leaves the screen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync(CancellationToken cancellationToken)
        => RunAsync(
            UpdateAction.Download,
            async () =>
            {
                if (_update.PageUrl is not { } link)
                {
                    return;
                }

                LibraryDownload placed;
                try
                {
                    placed = await _services.Downloads.AddAsync(link, cancellationToken);
                }
                catch (Exception exception) when (LinkProblems.Describe(exception, library: true) is { } problem)
                {
                    Tell(problem, isProblem: true);
                    return;
                }

                Tell(
                    placed.Queued switch
                    {
                        null => Text.Format(Strings.UpdateInLibraryAlready, placed.Path),
                        { AlreadyQueued: true } => Strings.UpdateDownloadingAlready,
                        _ => Text.Format(Strings.UpdateDownloading, Path.GetDirectoryName(placed.Path)),
                    },
                    isProblem: false);
            },
            cancellationToken);

    /// <summary>
    /// Shows or hides the update's changes: its version's entry in its mod's changelog, read from the site the first time
    /// any update of the mod asks.
    /// </summary>
    [RelayCommand]
    private Task ToggleChangesAsync(CancellationToken cancellationToken)
        => RunAsync(
            UpdateAction.Changes,
            async () =>
            {
                AreChangesShown = !AreChangesShown;
                if (!AreChangesShown)
                {
                    return;
                }

                if (_update.VersionLabel is not { } version)
                {
                    ChangesNote = Strings.ChangelogNoVersion;
                    return;
                }

                ChangesNote = Strings.ChangelogReading;
                IReadOnlyList<string>? changes;
                try
                {
                    changes = await _services.Lookup.GetChangesAsync(File.Mod, version, cancellationToken);
                }
                catch (AuthenticationFailedException)
                {
                    ChangesNote = Strings.ChangelogNeedsAccount;
                    return;
                }
                catch (HttpRequestException exception)
                {
                    ChangesNote = Text.Format(Strings.ChangelogFailed, exception.Message);
                    return;
                }

                if (changes is null)
                {
                    string site = _services.Sources.Find<IModSource>(File.Mod.Game.Source)?.DisplayName ?? File.Mod.Game.Source.Value;
                    ChangesNote = Text.Format(Strings.ChangelogNone, site);
                    return;
                }

                Changes = changes;
                ChangesNote = Changes.Count == 0 ? Strings.ChangelogNothing : string.Empty;
            },
            cancellationToken);

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    private Task RunAsync(UpdateAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, File.Id, File.Mod.Id), cancellationToken);
}
