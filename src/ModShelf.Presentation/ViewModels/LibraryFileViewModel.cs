using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// One of a mod's files in the library: its name, version, size and when it was downloaded, and deleting it.
/// </summary>
public sealed partial class LibraryFileViewModel : ObservableObject
{
    private readonly LibraryServices _services;

    internal LibraryFileViewModel(LibraryFile file, LibraryServices services)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        File = file.File;
        Show(file);
    }

    public ModFileRef File { get; }

    [ObservableProperty]
    public partial string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// The file's name on its site, such as "SkyUI 5.2".
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Version 5.2"; empty when the site gives none.
    /// </summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Downloaded 27/10/2026", in the user's time zone and culture.
    /// </summary>
    [ObservableProperty]
    public partial string DownloadedText { get; private set; } = string.Empty;

    /// <summary>
    /// Shows the file as the library has it now. Runs on the UI thread.
    /// </summary>
    internal void Show(LibraryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        FileName = file.FileName;
        Name = file.Name;
        VersionText = file.VersionLabel is { } version ? Text.Format(Strings.LibraryFileVersion, version) : string.Empty;
        SizeText = Sizes.Format(file.SizeInBytes);
        DateTimeOffset downloaded = TimeZoneInfo.ConvertTime(file.DownloadedAt, _services.TimeProvider.LocalTimeZone);
        DownloadedText = Text.Format(Strings.LibraryFileDownloaded, downloaded.ToString("d", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// Deletes the file from disk, for good, and from the library, once the user confirms. The screen shows the change
    /// once the library makes it.
    /// </summary>
    [RelayCommand]
    private Task DeleteAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            async () =>
            {
                string message = Text.Format(Strings.DeleteFileMessage, FileName, SizeText);
                if (!await _services.Dialogs.ConfirmAsync(Strings.DeleteFileTitle, message, Strings.DeleteFileConfirm, cancellationToken))
                {
                    return;
                }

                try
                {
                    await _services.Library.DeleteFileAsync(File, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    string reason = exception is InvalidDataException ? exception.Message : Text.Format(Strings.DeleteFileFailed, FileName, exception.Message);
                    await _services.Dialogs.ShowMessageAsync(Strings.DeleteFileFailedTitle, reason, cancellationToken);
                }
            },
            _services.Dialogs,
            exception => LogDeleteFailed(_services.Logger, exception, File.Id, File.Mod.Id),
            cancellationToken);
}
