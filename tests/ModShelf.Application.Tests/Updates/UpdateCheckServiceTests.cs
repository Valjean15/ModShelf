using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Updates;

/// <summary>
/// The background checks, on a clock the test moves.
/// </summary>
public sealed class UpdateCheckServiceTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly CountingChecker _checker = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly RecordingLogger<UpdateCheckService> _logger = new();
    private readonly SettingsService _settings;

    public UpdateCheckServiceTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task ChecksAtOnceWhenAWatchedModWasNeverChecked()
    {
        _library.Add(Watched("1", checkedAt: TestData.Now.AddMinutes(-5)));
        _library.Add(Watched("2", checkedAt: null));

        await RunAsync(async () => await WaitForChecksAsync(1));
    }

    [Fact]
    public async Task WaitsUntilTheOldestCheckIsAnIntervalOld()
    {
        _library.Add(Watched("1", checkedAt: TestData.Now.AddHours(-2)));
        _library.Add(Watched("2", checkedAt: TestData.Now.AddHours(-1)));

        await RunAsync(async () =>
        {
            await SettleAsync();
            _time.Advance(TimeSpan.FromHours(4) - TimeSpan.FromSeconds(1));
            await SettleAsync();
            Assert.Equal(0, _checker.Checks);

            _time.Advance(TimeSpan.FromSeconds(1));
            await WaitForChecksAsync(1);
        });
    }

    [Fact]
    public async Task ChecksAgainEveryInterval()
    {
        _store.Settings = new AppSettings { UpdateInterval = TimeSpan.FromMinutes(30) };

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            _time.Advance(TimeSpan.FromMinutes(29));
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);

            _time.Advance(TimeSpan.FromMinutes(1));
            await WaitForChecksAsync(2);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoesOnAfterACheckFails(bool unexpected)
    {
        _checker.Failure = unexpected ? new InvalidOperationException("Something broke.") : new InvalidDataException("The library can't be read.");

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            _time.Advance(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: The background check for updates failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsThatCantBeReadMeanTheDefault()
    {
        _store.Failure = new InvalidDataException("Not JSON.");

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            _time.Advance(UpdateInterval.Default - TimeSpan.FromMinutes(1));
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);
            _time.Advance(TimeSpan.FromMinutes(1));
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read how often to check for updates", StringComparison.Ordinal));
    }

    private static LibraryMod Watched(string mod, DateTimeOffset? checkedAt)
    {
        LibraryMod watched = LibraryMod.Watch(FakeModSite.Mod(mod), $"Mod {mod}", null, null, TestData.Now.AddDays(-9));
        return checkedAt is { } at ? watched.Checked(at) : watched;
    }

    /// <summary>
    /// Lets the service's loop run as far as it can on the current time.
    /// </summary>
    private static Task SettleAsync() => Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);

    private async Task WaitForChecksAsync(int checks)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (_checker.Checks < checks)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }

        Assert.Equal(checks, _checker.Checks);
    }

    private async Task RunAsync(Func<Task> test)
    {
        using UpdateCheckService service = new(_checker, _settings, _library, _time, _logger);
        await service.StartAsync(Cancellation);
        try
        {
            await test();
        }
        finally
        {
            await service.StopAsync(Cancellation);
        }

        Assert.True(service.ExecuteTask!.IsCompleted);
    }

    /// <summary>
    /// A checker that counts its checks, and fails each as the test says.
    /// </summary>
    private sealed class CountingChecker : IUpdateChecker
    {
        private int _checks;

        public int Checks => Volatile.Read(ref _checks);

        public Exception? Failure { get; set; }

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _checks);
            return Failure is { } failure ? Task.FromException<UpdateCheck>(failure) : Task.FromResult(new UpdateCheck(0, 0, [], []));
        }
    }
}
