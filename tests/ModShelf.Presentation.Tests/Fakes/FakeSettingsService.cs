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

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string> GetLibraryRootAsync(CancellationToken cancellationToken) => Task.FromResult(LibraryRoot);

    public Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken) => throw new NotSupportedException();
}
