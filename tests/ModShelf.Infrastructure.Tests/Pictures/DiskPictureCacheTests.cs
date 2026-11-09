using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Settings;
using ModShelf.Infrastructure.Pictures;
using ModShelf.Infrastructure.Settings;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Pictures;

public sealed class DiskPictureCacheTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private static readonly Uri Picture = new("https://staticdelivery.nexusmods.com/mods/1303/images/thumbnails/2400/picture.png");
    private static readonly Uri Other = new("https://staticdelivery.nexusmods.com/mods/1303/images/thumbnails/2401/other.png");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly TempFolder _folder = new();
    private readonly ScriptedHttpHandler _site = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<DiskPictureCache> _logger = new();
    private readonly JsonSettingsStore _settings;
    private readonly DiskPictureCache _pictures;

    public DiskPictureCacheTests()
    {
        _settings = new JsonSettingsStore(Path.Combine(_folder.Path, "settings.json"));
        _pictures = new DiskPictureCache(
            new SingleHandlerHttpClientFactory(_site),
            _settings,
            new AppFolders(_folder.Path, Path.Combine(_folder.Path, "logs")),
            _time,
            _logger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _pictures.Dispose();
        _site.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task DownloadsAPictureOnceAndKeepsIt()
    {
        _site.Respond(() => Image(Png));

        string? first = await _pictures.GetAsync(Picture, Cancellation);
        string? second = await _pictures.GetAsync(Picture, Cancellation);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(Path.Combine(_folder.Path, "pictures", DiskPictureCache.NameOf(Picture)), first);
        Assert.Equal(Png, await File.ReadAllBytesAsync(first, Cancellation));
        Assert.Equal(Picture, Assert.Single(_site.Requests).Uri);
    }

    [Fact]
    public async Task ASavedPictureIsFoundOffTheCallersThread()
    {
        ClockReaders clock = new(_time);
        using DiskPictureCache pictures = new(
            new SingleHandlerHttpClientFactory(_site),
            _settings,
            new AppFolders(_folder.Path, Path.Combine(_folder.Path, "logs")),
            clock,
            _logger);
        _site.Respond(() => Image(Png));
        await pictures.GetAsync(Picture, Cancellation);
        clock.Threads.Clear();

        // Marking it as just shown reads the clock, wherever the file's looked at.
        using (CallingThread caller = new(() => pictures.GetAsync(Picture, Cancellation)))
        {
            await caller.Work;

            Assert.NotEmpty(clock.Threads);
            Assert.DoesNotContain(caller.Id, clock.Threads);
        }

        using CallingThread sizing = new(() => pictures.GetSizeAsync(Cancellation));
        Assert.Equal(Png.Length, await (Task<long>)sizing.Work);
    }

    [Fact]
    public async Task CallersAskingAtOnceShareOneDownload()
    {
        HeldSite site = new();
        using DiskPictureCache pictures = new(
            new SingleHandlerHttpClientFactory(site),
            _settings,
            new AppFolders(_folder.Path, Path.Combine(_folder.Path, "logs")),
            _time,
            _logger);

        Task<string?> first = pictures.GetAsync(Picture, Cancellation);
        Task<string?> second = pictures.GetAsync(Picture, Cancellation);
        await site.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        site.Release();

        Assert.Equal(await first, await second);
        Assert.Equal(1, site.Requests);
        site.Dispose();
    }

    [Fact]
    public async Task ACallerThatStopsWaitingDoesntStopTheDownloadForTheOthers()
    {
        HeldSite site = new();
        using DiskPictureCache pictures = new(
            new SingleHandlerHttpClientFactory(site),
            _settings,
            new AppFolders(_folder.Path, Path.Combine(_folder.Path, "logs")),
            _time,
            _logger);
        using CancellationTokenSource leaving = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        Task<string?> left = pictures.GetAsync(Picture, leaving.Token);
        Task<string?> staying = pictures.GetAsync(Picture, Cancellation);
        await site.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        await leaving.CancelAsync();
        site.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => left);
        Assert.NotNull(await staying);
        site.Dispose();
    }

    [Fact]
    public async Task DownloadsAFewPicturesAtOnce()
    {
        HeldSite site = new();
        using DiskPictureCache pictures = new(
            new SingleHandlerHttpClientFactory(site),
            _settings,
            new AppFolders(_folder.Path, Path.Combine(_folder.Path, "logs")),
            _time,
            _logger);

        Task<string?>[] asked = [.. Enumerable.Range(1, DiskPictureCache.DownloadsAtOnce + 2)
            .Select(number => pictures.GetAsync(new Uri($"https://staticdelivery.nexusmods.com/mods/1303/images/{number}.png"), Cancellation))];
        await site.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        int askedAtFirst = await site.RequestsOnceSettledAsync(DiskPictureCache.DownloadsAtOnce);
        site.Release();

        Assert.All(await Task.WhenAll(asked), Assert.NotNull);
        Assert.Equal(DiskPictureCache.DownloadsAtOnce, askedAtFirst);
        Assert.Equal(DiskPictureCache.DownloadsAtOnce + 2, site.Requests);
        site.Dispose();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/png")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    [InlineData(HttpStatusCode.OK, null)]
    public async Task KeepsNothingTheSiteDoesntSendAsAPicture(HttpStatusCode status, string? type)
    {
        _site.Respond(() => Answer(status, Png, type));

        string? path = await _pictures.GetAsync(Picture, Cancellation);

        Assert.Null(path);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task KeepsNoPictureLargerThanTheLimitWhetherOrNotTheSiteSaysItsSize()
    {
        byte[] large = new byte[DiskPictureCache.MaxPictureBytes + 1];
        _site.Respond(() => Image(large)).Respond(() => Image(large, sayLength: false));

        string? announced = await _pictures.GetAsync(Picture, Cancellation);
        string? sent = await _pictures.GetAsync(Picture, Cancellation);

        Assert.Equal((null, null), (announced, sent));
        Assert.Empty(Files());
    }

    [Fact]
    public async Task DownloadsNoPictureTheSiteSaysIsLargerThanTheLimit()
    {
        _site.Respond(() =>
        {
            HttpResponseMessage response = Image(Png);
            response.Content.Headers.ContentLength = DiskPictureCache.MaxPictureBytes + 1;
            return response;
        });

        string? path = await _pictures.GetAsync(Picture, Cancellation);

        Assert.Null(path);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task APictureThatCouldntBeDownloadedIsAskedForAgainLater()
    {
        _site.Throw(new HttpRequestException("No route to host.")).Respond(() => Image(Png));

        string? offline = await _pictures.GetAsync(Picture, Cancellation);
        string? online = await _pictures.GetAsync(Picture, Cancellation);

        Assert.Null(offline);
        Assert.NotNull(online);
        Assert.Equal(2, _site.Requests.Count);
    }

    [Fact]
    public async Task LogsTheSiteNeverThePicturesAddress()
    {
        _site.Throw(new HttpRequestException("No route to host.")).Respond(() => Answer(HttpStatusCode.Forbidden, Png, "image/png"));

        await _pictures.GetAsync(Picture, Cancellation);
        await _pictures.GetAsync(Picture, Cancellation);

        Assert.Equal(2, _logger.Entries.Count);
        Assert.All(_logger.Entries, entry => Assert.Contains("staticdelivery.nexusmods.com", entry, StringComparison.Ordinal));
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("picture.png", entry, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/picture.png")]
    public async Task RefusesAddressesThatArentWebOnes(string address)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _pictures.GetAsync(new Uri(address), Cancellation));

        Assert.Empty(_site.Requests);
    }

    [Fact]
    public async Task DropsThePicturesShownLeastRecentlyWhenTheyTakeMoreRoomThanTheSettingsGive()
    {
        await _settings.SaveAsync(new AppSettings { PicturesMegabytes = AppSettings.MinPicturesMegabytes }, Cancellation);
        string oldest = Saved("oldest", 30 * Megabyte, _time.GetUtcNow().AddDays(-3));
        string older = Saved("older", 30 * Megabyte, _time.GetUtcNow().AddDays(-2));
        string recent = Saved("recent", 30 * Megabyte, _time.GetUtcNow().AddDays(-1));
        _site.Respond(() => Image(Png));

        string? added = await _pictures.GetAsync(Picture, Cancellation);

        // 90 MB and the new picture are more than 64; down to 57.6, the two oldest go.
        Assert.NotNull(added);
        Assert.Equal([Path.GetFileName(added), Path.GetFileName(recent)], Files().Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
    }

    [Fact]
    public async Task GivesThePicturesTheDefaultRoomWhenTheSettingsCantBeRead()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder.Path, "settings.json"), "{ not json", Cancellation);
        Saved("one", 30 * Megabyte, _time.GetUtcNow().AddDays(-3));
        Saved("two", 30 * Megabyte, _time.GetUtcNow().AddDays(-2));
        Saved("three", 30 * Megabyte, _time.GetUtcNow().AddDays(-1));
        _site.Respond(() => Image(Png));

        string? added = await _pictures.GetAsync(Picture, Cancellation);

        // 90 MB and the new picture are less than 512.
        Assert.NotNull(added);
        Assert.Equal(4, Files().Length);
    }

    [Fact]
    public async Task KeepsThePictureJustSavedWhenOthersSeemShownLater()
    {
        // Shown "later" than now, by a clock that was set back since.
        await _settings.SaveAsync(new AppSettings { PicturesMegabytes = AppSettings.MinPicturesMegabytes }, Cancellation);
        Saved("one", 30 * Megabyte, _time.GetUtcNow().AddDays(1));
        Saved("two", 30 * Megabyte, _time.GetUtcNow().AddDays(2));
        Saved("three", 30 * Megabyte, _time.GetUtcNow().AddDays(3));
        _site.Respond(() => Image(Png));

        string? added = await _pictures.GetAsync(Picture, Cancellation);

        Assert.True(File.Exists(added));
        Assert.Equal(2, Files().Length);
    }

    [Fact]
    public async Task APictureShownIsAmongTheLastToGo()
    {
        await _settings.SaveAsync(new AppSettings { PicturesMegabytes = AppSettings.MinPicturesMegabytes }, Cancellation);
        string shownAgain = Saved(DiskPictureCache.NameOf(Other), 30 * Megabyte, _time.GetUtcNow().AddDays(-3));
        string older = Saved("older", 30 * Megabyte, _time.GetUtcNow().AddDays(-2));
        string recent = Saved("recent", 30 * Megabyte, _time.GetUtcNow().AddDays(-1));
        _site.Respond(() => Image(Png));

        Assert.Equal(shownAgain, await _pictures.GetAsync(Other, Cancellation));
        await _pictures.GetAsync(Picture, Cancellation);

        Assert.True(File.Exists(shownAgain));
        Assert.False(File.Exists(older));
        Assert.False(File.Exists(recent));
        Assert.Single(_site.Requests);
    }

    [Fact]
    public async Task SaysHowMuchRoomThePicturesTakeAndClearsThem()
    {
        Saved("one", 3 * Megabyte, _time.GetUtcNow());
        Saved("two", 2 * Megabyte, _time.GetUtcNow());
        File.WriteAllBytes(Path.Combine(_folder.Path, "pictures", "three.0123.part"), new byte[1000]);

        long size = await _pictures.GetSizeAsync(Cancellation);
        await _pictures.ClearAsync(Cancellation);

        Assert.Equal(5 * Megabyte, size);
        Assert.Equal(0, await _pictures.GetSizeAsync(Cancellation));
    }

    [Fact]
    public async Task HasNoPicturesBeforeTheFirst()
    {
        Assert.Equal(0, await _pictures.GetSizeAsync(Cancellation));
        await _pictures.ClearAsync(Cancellation);
    }

    private static HttpResponseMessage Image(byte[] bytes, bool sayLength = true)
        => Answer(HttpStatusCode.OK, bytes, "image/png", sayLength);

    private static HttpResponseMessage Answer(HttpStatusCode status, byte[] bytes, string? type, bool sayLength = true)
    {
        HttpContent content = sayLength ? new ByteArrayContent(bytes) : new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = type is null ? null : new MediaTypeHeaderValue(type);
        content.Headers.ContentLength = sayLength ? bytes.Length : null;
        return new HttpResponseMessage(status) { Content = content };
    }

    /// <summary>
    /// A saved picture of that size, last shown then; sparse, so it costs no disk.
    /// </summary>
    private string Saved(string name, long size, DateTimeOffset shown)
    {
        string folder = Path.Combine(_folder.Path, "pictures");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        using (FileStream file = File.Create(path))
        {
            file.SetLength(size);
        }

        File.SetLastWriteTimeUtc(path, shown.UtcDateTime);
        return path;
    }

    private string[] Files()
    {
        string folder = Path.Combine(_folder.Path, "pictures");
        return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path)!)] : [];
    }

    /// <summary>
    /// A site that holds its answers until the test releases them, counting what it's asked.
    /// </summary>
    private sealed class HeldSite : HttpMessageHandler
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Requests => Volatile.Read(ref _requests);

        public void Release() => _released.TrySetResult();

        /// <summary>
        /// How many requests it holds once <paramref name="expected"/> arrived and a moment passed for any more.
        /// </summary>
        public async Task<int> RequestsOnceSettledAsync(int expected)
        {
            using CancellationTokenSource patience = new(TimeSpan.FromSeconds(10));
            while (Requests < expected)
            {
                await Task.Delay(10, patience.Token);
            }

            await Task.Delay(100, patience.Token);
            return Requests;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            Asked.TrySetResult();

            // A site answers whether or not anyone still waits, as the one download does for every caller.
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            return Image(Png);
        }
    }

    /// <summary>
    /// The test's clock, noting the thread of each reading.
    /// </summary>
    private sealed class ClockReaders(TimeProvider clock) : TimeProvider
    {
        public ConcurrentQueue<int> Threads { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            return clock.GetUtcNow();
        }
    }
}
