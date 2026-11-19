using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Contents;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Show contents (ADR 0106): what one of the library's archives holds, as a tree of its folders and files with their
/// sizes, and the text file chosen in it, its readme first, read where the archive is. Nothing is extracted.
/// </summary>
public sealed partial class FileContentsViewModel : ObservableObject
{
    private readonly ILibraryContents _contents;
    private readonly ModFileRef _file;
    private readonly ILogger _logger;
    private readonly List<ContentRowViewModel> _roots = [];
    private CancellationTokenSource? _reading;

    /// <param name="fileName">The archive's name in the library, for the title.</param>
    public FileContentsViewModel(ILibraryContents contents, ModFileRef file, string fileName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(logger);

        _contents = contents;
        _file = file;
        _logger = logger;
        Title = Text.Format(Strings.ContentsTitle, fileName);
    }

    /// <summary>
    /// Asks the dialog showing it to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string Title { get; }

    /// <summary>
    /// Whether it's still reading the archive's list.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListed))]
    public partial bool IsListing { get; private set; }

    /// <summary>
    /// Such as "12 files in 3 folders, 4.20 MB unpacked, in a 7z archive."
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListed))]
    public partial string? SummaryText { get; private set; }

    /// <summary>
    /// Why the archive can't be shown; null while it can.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListed))]
    public partial string? Problem { get; private set; }

    public bool IsListed => !IsListing && Problem is null && SummaryText is not null;

    /// <summary>
    /// The rows shown: the folders at the top, and what's in each open folder, below it.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ContentRowViewModel> Rows { get; private set; } = [];

    /// <summary>
    /// The row chosen; a text file's is read and shown.
    /// </summary>
    [ObservableProperty]
    public partial ContentRowViewModel? SelectedRow { get; set; }

    /// <summary>
    /// The text file shown's path; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    public partial string? TextTitle { get; private set; }

    [ObservableProperty]
    public partial string TextContent { get; private set; } = string.Empty;

    /// <summary>
    /// That only its start is shown, when it's larger than ModShelf reads; or why it can't be shown.
    /// </summary>
    [ObservableProperty]
    public partial string? TextNote { get; private set; }

    public bool HasText => TextTitle is not null;

    /// <summary>
    /// Listing the archive, as the dialog opens; for tests to wait on.
    /// </summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reading the text file chosen; for tests to wait on.
    /// </summary>
    internal Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts listing the archive, as the dialog opens.
    /// </summary>
    public void StartListing() => Listing = ListAsync();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Opens every folder, or closes them all when every one is open.
    /// </summary>
    [RelayCommand]
    private void ToggleAll()
    {
        List<ContentRowViewModel> folders = [.. Folders(_roots)];
        bool open = !folders.TrueForAll(folder => folder.IsExpanded);
        folders.ForEach(folder => folder.IsExpanded = open);
        ShowRows();
    }

    partial void OnSelectedRowChanged(ContentRowViewModel? value)
    {
        if (value is { IsText: true })
        {
            Reading = ReadAsync(value);
        }
    }

    private static IEnumerable<ContentRowViewModel> Folders(IEnumerable<ContentRowViewModel> rows)
        => rows.Where(row => row.IsFolder).SelectMany(folder => Folders(folder.Children).Prepend(folder));

    private static void Visible(IEnumerable<ContentRowViewModel> rows, List<ContentRowViewModel> shown)
    {
        foreach (ContentRowViewModel row in rows)
        {
            shown.Add(row);
            if (row.IsExpanded)
            {
                Visible(row.Children, shown);
            }
        }
    }

    /// <summary>
    /// Each folder's files' sizes together.
    /// </summary>
    private static long Total(ContentRowViewModel row)
    {
        if (row.IsFolder)
        {
            row.SizeInBytes = row.Children.Sum(Total);
        }

        return row.SizeInBytes;
    }

    private async Task ListAsync()
    {
        IsListing = true;
        try
        {
            ArchiveContents listed = await _contents.ListAsync(_file, CancellationToken.None);
            Build(listed.Entries);
            int folders = listed.Entries.Count - listed.Files;
            SummaryText = Text.Format(Strings.ContentsSummary, Counts.Of(listed.Files, Strings.ContentsOneFile, Strings.ContentsFiles), Counts.Of(folders, Strings.ContentsOneFolder, Strings.ContentsFolders), Sizes.Format(listed.UnpackedSize), listed.Format);
            ContentRowViewModel? readme = Open(listed.Readme);
            ShowRows();
            SelectedRow = readme;
        }
        catch (Exception exception) when (Said(exception) is { } said)
        {
            LogListFailed(_logger, exception);
            Problem = said;
        }
        finally
        {
            IsListing = false;
        }
    }

    private async Task ReadAsync(ContentRowViewModel row)
    {
        if (_reading is { } previous)
        {
            await previous.CancelAsync();
        }

        using CancellationTokenSource reading = new();
        _reading = reading;
        TextTitle = row.Path;
        TextContent = string.Empty;
        TextNote = Strings.ContentsReading;
        try
        {
            ArchiveText text = await _contents.ReadTextAsync(_file, row.Path, reading.Token);
            if (!reading.IsCancellationRequested)
            {
                TextContent = text.Text;
                TextNote = text.IsCut ? Text.Format(Strings.ContentsTextCut, Sizes.Format(ILibraryContents.MaxTextBytes), Sizes.Format(text.SizeInBytes)) : null;
            }
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
            // Another file was chosen meanwhile.
        }
        catch (Exception exception) when (Said(exception) is { } said)
        {
            LogReadFailed(_logger, exception);
            TextNote = said;
        }
        finally
        {
            if (ReferenceEquals(_reading, reading))
            {
                _reading = null;
            }
        }
    }

    /// <summary>
    /// The tree of rows, every folder closed, then the top folder opened when it's the only thing at the top.
    /// </summary>
    private void Build(IReadOnlyList<ArchiveEntry> entries)
    {
        Dictionary<string, ContentRowViewModel> folders = new(StringComparer.Ordinal);
        foreach (ArchiveEntry entry in entries)
        {
            ContentRowViewModel row = new(entry, _ => ShowRows()) { SizeInBytes = entry.SizeInBytes };
            if (entry.IsFolder)
            {
                folders[entry.Path] = row;
            }

            (entry.Folder.Length > 0 && folders.TryGetValue(entry.Folder, out ContentRowViewModel? parent) ? parent.Children : _roots).Add(row);
        }

        _roots.ForEach(row => Total(row));
        if (_roots is [{ IsFolder: true } only])
        {
            only.IsExpanded = true;
        }
    }

    /// <summary>
    /// Opens the folders the entry at <paramref name="path"/> is in, so its row shows.
    /// </summary>
    /// <returns>Its row; null for none.</returns>
    private ContentRowViewModel? Open(string? path)
    {
        List<ContentRowViewModel> level = _roots;
        ContentRowViewModel? found = null;
        foreach (string part in path?.Split('/') ?? [])
        {
            found = level.Find(row => string.Equals(row.Name, part, StringComparison.Ordinal));
            if (found is null)
            {
                return null;
            }

            if (found.IsFolder)
            {
                found.IsExpanded = true;
            }

            level = found.Children;
        }

        return found;
    }

    private void ShowRows()
    {
        List<ContentRowViewModel> shown = [];
        Visible(_roots, shown);
        Rows = shown;
    }

    /// <summary>
    /// What to tell the user of a problem; null for one that isn't theirs to know of.
    /// </summary>
    private static string? Said(Exception exception) => exception switch
    {
        ArchiveUnreadableException { Trouble: ArchiveTrouble.Encrypted } => Strings.ContentsEncrypted,
        ArchiveUnreadableException { Trouble: ArchiveTrouble.NotText } => Strings.ContentsNotText,
        ArchiveUnreadableException { Trouble: ArchiveTrouble.NotAnArchive } => Strings.ContentsNotAnArchive,
        ArchiveUnreadableException => Strings.ContentsBroken,
        FileNotFoundException => Strings.ContentsMissing,
        KeyNotFoundException or IOException or UnauthorizedAccessException or InvalidDataException => Text.Format(Strings.ContentsCantRead, exception.Message),
        _ => null,
    };
}
