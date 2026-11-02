using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// One of the library's files of the mod on its page: what it is, where it is, whether it's still there, and its SHA-256.
/// </summary>
public sealed partial class OwnFileViewModel
{
    /// <summary>
    /// The folder old versions are moved into, beside their mod's files, when the settings say to archive them.
    /// </summary>
    private const string ArchiveFolder = "_archive";

    /// <summary>
    /// How many of the SHA-256's characters are shown at each end of it, with the rest left out.
    /// </summary>
    private const int Sha256Ends = 8;

    private readonly Func<string, CancellationToken, Task> _copy;
    private readonly Func<OwnFileViewModel, CancellationToken, Task> _delete;

    /// <param name="isMissing">Whether the file isn't on disk where the library says it is.</param>
    /// <param name="copy">Puts text on the clipboard, saying so when it can't.</param>
    /// <param name="delete">Deletes the file from disk and the library, once the user confirms.</param>
    internal OwnFileViewModel(
        LibraryFile file,
        bool isMissing,
        TimeProvider timeProvider,
        Func<string, CancellationToken, Task> copy,
        Func<OwnFileViewModel, CancellationToken, Task> delete)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(delete);

        File = file.File;
        Name = file.Name;
        CategoryText = CategoryName(file.Category);
        IsArchived = IsInArchive(file.RelativePath);
        FileName = IsArchived ? $"{ArchiveFolder}{LibraryPaths.Separator}{file.FileName}" : file.FileName;
        SizeText = Sizes.Format(file.SizeInBytes);
        DownloadedText = Text.Format(Strings.LibraryFileDownloaded, Dates.Day(file.DownloadedAt, timeProvider));
        Sha256 = file.Sha256 ?? string.Empty;
        Sha256Text = Sha256.Length > (Sha256Ends * 2) + 1 ? $"{Sha256[..Sha256Ends]}…{Sha256[^Sha256Ends..]}" : Sha256;
        IsMissing = isMissing;
        _copy = copy;
        _delete = delete;
    }

    public ModFileRef File { get; }

    /// <summary>
    /// Its name on its site, such as "SkyUI 5.2".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Its section on its site, such as "Main" or "Optional".
    /// </summary>
    public string CategoryText { get; }

    /// <summary>
    /// The file's name in its mod's folder; in <c>_archive</c> for an old version moved there.
    /// </summary>
    public string FileName { get; }

    public string SizeText { get; }

    /// <summary>
    /// Such as "Downloaded 10/12/2026", in the user's time zone and culture.
    /// </summary>
    public string DownloadedText { get; }

    /// <summary>
    /// Its SHA-256, to check a copy against; empty for a file recorded without one.
    /// </summary>
    public string Sha256 { get; }

    public bool HasSha256 => Sha256.Length > 0;

    /// <summary>
    /// Its SHA-256 shortened to its ends, such as "3f9a61c0…8be2c21e", to show; <see cref="CopySha256Command"/> copies it
    /// whole.
    /// </summary>
    public string Sha256Text { get; }

    /// <summary>
    /// Whether it's an old version moved to the <c>_archive</c> folder.
    /// </summary>
    public bool IsArchived { get; }

    /// <summary>
    /// Whether it isn't on disk where the library says it is.
    /// </summary>
    public bool IsMissing { get; }

    /// <summary>
    /// Puts the file's whole SHA-256 on the clipboard.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSha256))]
    private Task CopySha256Async(CancellationToken cancellationToken) => _copy(Sha256, cancellationToken);

    /// <summary>
    /// Deletes the file from disk, for good, and from the library, once the user confirms.
    /// </summary>
    [RelayCommand]
    private Task DeleteAsync(CancellationToken cancellationToken) => _delete(this, cancellationToken);

    /// <summary>
    /// Whether a library path is in an <c>_archive</c> folder.
    /// </summary>
    internal static bool IsInArchive(string relativePath)
    {
        string[] parts = relativePath.Split(LibraryPaths.Separator);
        return parts.Length > 1 && string.Equals(parts[^2], ArchiveFolder, StringComparison.OrdinalIgnoreCase);
    }

    internal static string CategoryName(ModFileCategory category) => category switch
    {
        ModFileCategory.Main => Strings.FileCategoryMain,
        ModFileCategory.Update => Strings.FileCategoryUpdate,
        ModFileCategory.Optional => Strings.FileCategoryOptional,
        ModFileCategory.Miscellaneous => Strings.FileCategoryMiscellaneous,
        ModFileCategory.OldVersion => Strings.FileCategoryOldVersion,
        ModFileCategory.Archived => Strings.FileCategoryArchived,
        _ => Strings.FileCategoryUnknown,
    };
}
