using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// One of a mod's files on the Browse screen, as its site lists it, with downloading it into the library or a folder.
/// </summary>
public sealed partial class ModFileViewModel : ObservableObject
{
    private readonly ModFile _file;
    private readonly BrowseServices _services;

    /// <param name="isLinked">Whether it's the file the link the user gave is to.</param>
    /// <param name="modIsAvailable">Whether its mod can be downloaded from, rather than hidden or removed.</param>
    internal ModFileViewModel(ModFile file, bool isLinked, bool modIsAvailable, BrowseServices services)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(services);

        _file = file;
        _services = services;
        Name = file.Name;
        FileName = file.FileName;
        VersionText = file.VersionLabel is { } version ? Text.Format(Strings.LibraryFileVersion, version) : string.Empty;
        SizeText = file.SizeInBytes is { } size ? Sizes.Format(size) : string.Empty;
        UploadedText = Text.Format(Strings.UpdateUploaded, ModText.LocalDate(file.UploadedAt, services.TimeProvider));
        IsLinked = isLinked;
        CanDownload = modIsAvailable && file.PageUrl is not null;
    }

    /// <summary>
    /// What the user asked of a file, as the log names it.
    /// </summary>
    private enum FileAction
    {
        Download,
        SaveToFolder,
    }

    public ModFileRef File => _file.Reference;

    /// <summary>
    /// Its name on its site, such as "SkyUI 5.2".
    /// </summary>
    public string Name { get; }

    public string FileName { get; }

    /// <summary>
    /// Such as "Version 5.2"; empty when the site gives none.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Such as "2.1 MB"; empty when the site doesn't say.
    /// </summary>
    public string SizeText { get; }

    /// <summary>
    /// Such as "Uploaded 10/27/2026", in the user's time zone and culture.
    /// </summary>
    public string UploadedText { get; }

    /// <summary>
    /// Whether it's the file the link the user gave is to.
    /// </summary>
    public bool IsLinked { get; }

    /// <summary>
    /// Whether it can be downloaded: its mod is available, and the site gives a link to it.
    /// </summary>
    public bool CanDownload { get; }

    /// <summary>
    /// Whether the library has it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInLibrary { get; private set; }

    /// <summary>
    /// What became of downloading it, such as where it downloads to, or why it couldn't; null for nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Shows whether the library has the file now. Runs on the UI thread.
    /// </summary>
    internal void ShowLibrary(bool isInLibrary) => IsInLibrary = isInLibrary;

    /// <summary>
    /// Downloads the file into the library, where its mod's files are, or where its game's folder pattern says.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync(CancellationToken cancellationToken)
        => RunAsync(
            FileAction.Download,
            async () =>
            {
                LibraryDownload placed;
                try
                {
                    placed = await _services.LibraryDownloads.AddAsync(_file.PageUrl!, cancellationToken);
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
    /// Downloads the file into a folder the user picks, outside the library, as Save to a folder on the Downloads screen
    /// does.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task SaveToFolderAsync(CancellationToken cancellationToken)
        => RunAsync(
            FileAction.SaveToFolder,
            async () =>
            {
                LinkDownload? added;
                try
                {
                    added = await _services.Folders.AddAsync(_file.PageUrl!, cancellationToken);
                }
                catch (Exception exception) when (LinkProblems.Describe(exception, library: false) is { } problem)
                {
                    Tell(problem, isProblem: true);
                    return;
                }

                if (added is not null)
                {
                    Tell(
                        added switch
                        {
                            { AlreadySaved: true } => Text.Format(Strings.DownloadsSavedAlready, added.Job.FileName, added.Job.TargetDirectory),
                            { AlreadyQueued: true } => Strings.UpdateDownloadingAlready,
                            _ => Text.Format(Strings.DownloadsDownloadingInto, added.Job.FileName, added.Job.TargetDirectory),
                        },
                        isProblem: false);
                }
            },
            cancellationToken);

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    private Task RunAsync(FileAction action, Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunAsync(work, _services.Dialogs, exception => LogActionFailed(_services.Logger, exception, action, File.Id, File.Mod.Id), cancellationToken);
}
