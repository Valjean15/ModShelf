using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Library;

public sealed class LibraryRecorderTests : IDisposable
{
    private readonly FakeModSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<LibraryRecorder> _logger = new();
    private readonly SettingsService _settings;
    private readonly LibraryDownloads _libraryDownloads;

    public LibraryRecorderTests()
    {
        _settings = new SettingsService(new InMemorySettingsStore(), new FakeUserFolders(), _library);
        _libraryDownloads = new LibraryDownloads(
            new SourceRegistry([_site]),
            _downloads,
            _library,
            _settings,
            _storage,
            TimeProvider.System,
            new RecordingLogger<LibraryDownloads>());
        _site.AddMod("1", "First");
        _site.AddFile("1", "10", "first.7z");
        _site.AddMod("2", "Second");
        _site.AddFile("2", "20", "second.7z");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose()
    {
        _libraryDownloads.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public async Task RecordsTheMissedDownloadsAtStartThenEachAsItFinishes()
    {
        _downloads.Add(Done("1", "10", "first.7z"));
        _storage.Add(Path.Combine(Root, "first.7z"));
        using LibraryRecorder recorder = new(_libraryDownloads, _downloads, _logger);

        await recorder.StartAsync(Cancellation);
        await WaitUntilAsync(() => _library.Mods.Count == 1);
        DownloadJobSnapshot second = RecordingDownloadService.Job(File("2", "20"), Root, "second.7z", DownloadJobStatus.Downloading);
        _downloads.Update(second);
        _downloads.Update(RecordingDownloadService.Completed(second));
        await WaitUntilAsync(() => _library.Mods.Count == 2);
        await recorder.StopAsync(Cancellation);

        Assert.Equal(["first.7z", "second.7z"], _library.Mods.Select(mod => Assert.Single(mod.Files).FileName));
    }

    [Fact]
    public async Task GoesOnRecordingAfterADownloadItCantRecord()
    {
        using LibraryRecorder recorder = new(_libraryDownloads, _downloads, _logger);
        await recorder.StartAsync(Cancellation);

        _site.Failures["GetModAsync"] = new HttpRequestException("Unreachable.", null, HttpStatusCode.ServiceUnavailable);
        _downloads.Update(Done("1", "10", "first.7z"));
        _downloads.Update(Done("2", "20", "second.7z"));
        await WaitUntilAsync(() => _library.Mods.Count == 1);
        await recorder.StopAsync(Cancellation);

        Assert.Equal(Mod("2"), Assert.Single(_library.Mods).Mod);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't add the finished download first.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsWhenTheDownloadEngineStops()
    {
        using LibraryRecorder recorder = new(_libraryDownloads, _downloads, _logger);
        await recorder.StartAsync(Cancellation);

        _downloads.Stop();

        await recorder.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.True(recorder.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StopsWithTheHost()
    {
        using LibraryRecorder recorder = new(_libraryDownloads, _downloads, _logger);
        await recorder.StartAsync(Cancellation);

        await WaitUntilAsync(() => _downloads.Watched);
        await recorder.StopAsync(Cancellation);

        Assert.True(recorder.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(_logger.Entries);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    private DownloadJobSnapshot Done(string modId, string fileId, string fileName)
        => RecordingDownloadService.Completed(RecordingDownloadService.Job(File(modId, fileId), Root, fileName, DownloadJobStatus.Queued));
}
