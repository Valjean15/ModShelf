using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Settings with a library root, which is all the Downloads screen reads, and settings stored as the test says, which
/// the Library and Browse read for the hidden games and a game's own settings.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public string Location => "settings.json";

    public string DefaultLibraryRoot => TestData.LibraryRoot;

    public string LibraryRoot { get; set; } = TestData.LibraryRoot;

    /// <summary>
    /// What reading the settings throws, such as an <see cref="InvalidDataException"/>; null for nothing.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// The settings stored.
    /// </summary>
    public AppSettings Stored { get; set; } = new();

    /// <summary>
    /// What reading every setting throws, such as an <see cref="InvalidDataException"/>; null for nothing.
    /// </summary>
    public Exception? ReadRefusal { get; set; }

    /// <summary>
    /// When set, reading every setting waits for it.
    /// </summary>
    public TaskCompletionSource? ReadHeld { get; set; }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
    {
        if (ReadHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return ReadRefusal is null ? Stored : throw ReadRefusal;
    }

    public Task<string> GetLibraryRootAsync(CancellationToken cancellationToken)
        => Refusal is null ? Task.FromResult(LibraryRoot) : Task.FromException<string>(Refusal);

    public Task<LibraryRoots> GetLibraryRootsAsync(CancellationToken cancellationToken)
        => Refusal is null ? Task.FromResult(LibraryRoots.Of(Stored, LibraryRoot)) : Task.FromException<LibraryRoots>(Refusal);

    public Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetLibraryFoldersAsync(GameRef? game, FolderPattern? pattern, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetGameAsync(GameRef game, Func<GameSettings, GameSettings> change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CopyGameAsync(GameRef other, GameRef game, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetCloseToTrayAsync(bool? closeToTray, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetSetupDoneAsync(bool? done, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetCheckForAppUpdatesAsync(bool? check, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetPreviewAsync(PreviewScreen screen, PreviewLevel? level, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetPicturesMegabytesAsync(int? megabytes, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetDetailsMaxAgeAsync(TimeSpan? age, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetAdultPicturesAsync(AdultPictures? pictures, CancellationToken cancellationToken) => throw new NotSupportedException();
}
