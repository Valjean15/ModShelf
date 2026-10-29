using ModShelf.Application.Settings;
using ModShelf.Domain.Library;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Settings with a library root, which is all the Downloads screen reads.
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

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string> GetLibraryRootAsync(CancellationToken cancellationToken)
        => Refusal is null ? Task.FromResult(LibraryRoot) : Task.FromException<string>(Refusal);

    public Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken) => throw new NotSupportedException();
}
