using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels.Updates;

/// <summary>
/// One update on the Updates screen: a newer file of a watched mod, with its version's entry in the mod's changelog,
/// read once the screen is shown, and downloading it into the library.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdatesServices _services;
    private LibraryUpdate _update;
    private Task? _reading;
    private bool _changesRead;

    internal UpdateViewModel(LibraryUpdate update, LibraryMod mod, UpdatesServices services, ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pictures);

        _update = update;
        _services = services;
        File = update.File;
        Picture = new ModPictureViewModel(pictures);
        Show(update, mod);
    }

    /// <summary>
    /// What the user asked of an update, as the log names it.
    /// </summary>
    private enum UpdateAction
    {
        Download,
        Changes,
        Skip,
    }

    public ModFileRef File { get; }

    /// <summary>
    /// Its mod's picture, as its thumbnail shows it, while the screen shows pictures.
    /// </summary>
    public ModPictureViewModel Picture { get; }

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
    /// Such as "Replaces SkyUI_5_2_SE.7z"; empty when it replaces none of the library's files.
    /// </summary>
    [ObservableProperty]
    public partial string ReplacesText { get; private set; } = string.Empty;

    /// <summary>
    /// What it changes, such as "1.3 → 1.4" from the version of the file it replaces; "New optional file" for an optional
    /// file that replaces none; otherwise its version, such as "Version 1.4", or nothing.
    /// </summary>
    [ObservableProperty]
    public partial string ChangeText { get; private set; } = string.Empty;

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
    /// Such as "What changed in 1.4"; empty for a file without a version.
    /// </summary>
    [ObservableProperty]
    public partial string ChangesTitle { get; private set; } = string.Empty;

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
        VersionText = update.VersionLabel is { } version ? Text.Format(Strings.FileVersion, version) : string.Empty;
        UploadedText = Text.Format(Strings.FileUploaded, Dates.Day(update.UploadedAt, _services.TimeProvider));
        LibraryFile? replaced = mod.Files.FirstOrDefault(file => file.File == update.Replaces);
        ReplacesText = replaced is null ? string.Empty : Text.Format(Strings.UpdateReplaces, replaced.FileName);
        ChangeText = (replaced?.VersionLabel, update.VersionLabel) switch
        {
            ({ } before, { } after) => Text.Format(Strings.UpdateChange, before, after),
            _ when update.Replaces is null && update.Category == ModFileCategory.Optional => Strings.UpdateNewOptionalFile,
            _ => VersionText,
        };
        ChangesTitle = update.VersionLabel is { } changed ? Text.Format(Strings.ChangelogWhatChanged, changed) : string.Empty;
        CanDownload = update.PageUrl is not null;
    }

    /// <summary>
    /// Reads its version's entry in its mod's changelog, once: the site is asked again later only when it couldn't say
    /// this time. A read already under way is the one returned. Runs on the UI thread.
    /// </summary>
    /// <param name="gate">How many changelogs may be read at once.</param>
    internal Task ReadChangesAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);

        if (_changesRead || _reading is { IsCompleted: false })
        {
            return _reading ?? Task.CompletedTask;
        }

        _reading = ReadChangesOnceAsync(gate, cancellationToken);
        return _reading;
    }

    /// <summary>
    /// Downloads the update into the library, as Download does, as one of several: what went wrong unexpectedly is
    /// logged and said in its note, so the others go on.
    /// </summary>
    internal async Task QueueAsync(CancellationToken cancellationToken)
    {
        if (_update.PageUrl is not { } link)
        {
            return;
        }

        try
        {
            await DownloadNotes.AddToLibraryAsync(_services.Downloads, link, Tell, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogActionFailed(_services.Logger, exception, UpdateAction.Download, File.Id, File.Mod.Id);
            Tell(Strings.SomethingWentWrong, true);
        }
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
                if (_update.PageUrl is { } link)
                {
                    await DownloadNotes.AddToLibraryAsync(_services.Downloads, link, Tell, cancellationToken);
                }
            },
            cancellationToken);

    /// <summary>
    /// Skips the update until a newer one comes. Once the library has skipped it, it leaves the screen.
    /// </summary>
    [RelayCommand]
    private Task SkipAsync(CancellationToken cancellationToken)
        => RunAsync(
            UpdateAction.Skip,
            async () =>
            {
                try
                {
                    await _services.Library.SkipUpdateAsync(File, true, cancellationToken);
                }
                catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
                {
                    // The mod left the library, or the update did, meanwhile.
                }
            },
            cancellationToken);

    private async Task ReadChangesOnceAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        if (_update.VersionLabel is not { } version)
        {
            ChangesNote = Strings.ChangelogNoVersion;
            _changesRead = true;
            return;
        }

        ChangesNote = Strings.ChangelogReading;
        IReadOnlyList<string>? changes;
        try
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                changes = await _services.Lookup.GetChangesAsync(File.Mod, version, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
            return;
        }
        catch (AuthenticationFailedException)
        {
            ChangesNote = Strings.ChangelogNeedsAccount;
            return;
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            ChangesNote = Text.Format(Strings.ChangelogFailed, exception.Message);
            return;
        }
        catch (Exception exception)
        {
            LogActionFailed(_services.Logger, exception, UpdateAction.Changes, File.Id, File.Mod.Id);
            ChangesNote = Strings.ChangelogUnexpected;
            return;
        }

        _changesRead = true;
        if (changes is null)
        {
            ChangesNote = Text.Format(Strings.ChangelogNone, _services.Sources.SiteName(File.Mod.Game.Source));
            return;
        }

        Changes = changes;
        ChangesNote = Changes.Count == 0 ? Strings.ChangelogNothing : string.Empty;
    }

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    private Task RunAsync(UpdateAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, File.Id, File.Mod.Id), cancellationToken);
}
