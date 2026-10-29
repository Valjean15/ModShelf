using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
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
    private readonly RecordingNotificationService _notifications = new();
    private readonly RecordingLogger<UpdateCheckService> _logger = new();
    private readonly SettingsService _settings;
    private readonly TestClock _clock;

    public UpdateCheckServiceTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _clock = new TestClock(_time);
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
            await AdvanceAsync(TimeSpan.FromHours(4) - TimeSpan.FromSeconds(1));
            await SettleAsync();
            Assert.Equal(0, _checker.Checks);

            await AdvanceAsync(TimeSpan.FromSeconds(1));
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
            await AdvanceAsync(TimeSpan.FromMinutes(29));
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);

            await AdvanceAsync(TimeSpan.FromMinutes(1));
            await WaitForChecksAsync(2);
        });
    }

    [Fact]
    public async Task ALongerIntervalChosenWhileWaitingPutsTheNextCheckOff()
    {
        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(TimeSpan.FromHours(1));
            await _settings.SetUpdateIntervalAsync(TimeSpan.FromHours(12), Cancellation);
            await AdvanceAsync(UpdateInterval.Default);
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);
            await AdvanceAsync(TimeSpan.FromHours(5));
            await WaitForChecksAsync(2);
        });
    }

    [Fact]
    public async Task AShorterIntervalChosenWhileWaitingCountsWithinMinutes()
    {
        // Changed from a week to 30 minutes in Settings: the next check comes 30 minutes after the last, not a week on.
        _store.Settings = new AppSettings { UpdateInterval = UpdateInterval.Maximum };

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await _settings.SetUpdateIntervalAsync(TimeSpan.FromMinutes(30), Cancellation);
            await AdvanceAsync(UpdateInterval.Minimum);
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);

            await AdvanceAsync(TimeSpan.FromMinutes(15));
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
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: The background check for updates failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoesOnAfterACheckTimesOut()
    {
        _checker.Failure = new TaskCanceledException("The request timed out.");

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: The background check for updates failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALibraryThatCantBeReadMeansAFirstCheckAnIntervalFromNow()
    {
        _library.ListFailure = new InvalidOperationException("SQLite Error 11: 'database disk image is malformed'.");

        await RunAsync(async () =>
        {
            await SettleAsync();
            Assert.Equal(0, _checker.Checks);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(1);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: The background check for updates failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(400)]
    public async Task ACheckLaterThanNowMeansTheFirstCheckIsAnIntervalFromNow(int daysAhead)
    {
        // The clock has moved back since the mod was checked.
        _library.Add(Watched("1", checkedAt: TestData.Now.AddDays(daysAhead)));

        await RunAsync(async () =>
        {
            await SettleAsync();
            await AdvanceAsync(UpdateInterval.Default - TimeSpan.FromSeconds(1));
            await SettleAsync();
            Assert.Equal(0, _checker.Checks);
            await AdvanceAsync(TimeSpan.FromSeconds(1));
            await WaitForChecksAsync(1);
        });
    }

    [Fact]
    public async Task TheClockMovingBackAfterACheckDoesntPutTheNextOneOff()
    {
        _checker.OnCheck = check =>
        {
            if (check == 1)
            {
                _clock.MoveBack(TimeSpan.FromDays(365));
            }
        };

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });
    }

    [Fact]
    public async Task SettingsThatCantBeOpenedMeanTheDefault()
    {
        _store.Failure = new IOException("The file is in use.");

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read how often to check for updates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsThatCantBeReadMeanTheDefault()
    {
        _store.Failure = new InvalidDataException("Not JSON.");

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default - TimeSpan.FromMinutes(1));
            await SettleAsync();
            Assert.Equal(1, _checker.Checks);
            await AdvanceAsync(TimeSpan.FromMinutes(1));
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read how often to check for updates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowsANoticeOfNewUpdates()
    {
        _library.Add(Watched("1", checkedAt: null));
        _library.Add(Watched("2", checkedAt: null));
        _checker.Found = [Update("1", "10"), Update("2", "20")];

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await WaitForNoticesAsync(1);
        });

        Assert.Equal(new Notification("2 new updates", "Mod 1 and Mod 2"), Assert.Single(_notifications.Shown));
    }

    [Fact]
    public async Task ACheckWithoutNewUpdatesShowsNoNotice()
    {
        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await SettleAsync();
        });

        Assert.Empty(_notifications.Shown);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task ShowsNoNoticeWhenTheSettingsSayNot()
    {
        _store.Settings = new AppSettings { NotifyOfUpdates = false };
        _checker.Found = [Update("1", "10")];

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await SettleAsync();
        });

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task SettingsThatCantBeReadMeanANotice()
    {
        _store.Failure = new InvalidDataException("Not JSON.");
        _checker.Found = [Update("1", "10")];

        await RunAsync(async () => await WaitForNoticesAsync(1));

        Assert.Equal("1 new update", Assert.Single(_notifications.Shown).Title);
    }

    [Fact]
    public async Task SettingsThatCantBeOpenedMeanANotice()
    {
        _store.Failure = new IOException("The file is in use.");
        _checker.Found = [Update("1", "10")];

        await RunAsync(async () => await WaitForNoticesAsync(1));

        Assert.Equal("1 new update", Assert.Single(_notifications.Shown).Title);
    }

    [Fact]
    public async Task ANoticeThatTimesOutIsLoggedAndChecksGoOn()
    {
        _notifications.Failure = new TaskCanceledException("The notice didn't show in time.");
        _checker.Found = [Update("1", "10")];

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't show the notice of new updates (1 found)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANoticeThatCantBeShownIsLoggedAndChecksGoOn()
    {
        _notifications.Failure = new InvalidOperationException("No notices here.");
        _checker.Found = [Update("1", "10")];

        await RunAsync(async () =>
        {
            await WaitForChecksAsync(1);
            await AdvanceAsync(UpdateInterval.Default);
            await WaitForChecksAsync(2);
        });

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't show the notice of new updates (1 found)", StringComparison.Ordinal));
    }

    private static LibraryMod Watched(string mod, DateTimeOffset? checkedAt)
    {
        LibraryMod watched = LibraryMod.Watch(FakeModSite.Mod(mod), $"Mod {mod}", null, null, TestData.Now.AddDays(-9));
        return checkedAt is { } at ? watched.Checked(at) : watched;
    }

    private static LibraryUpdate Update(string mod, string file)
        => new(new ModFileRef(FakeModSite.Mod(mod), file), $"File {file}", $"{file}.7z", null, ModFileCategory.Main, TestData.Now, null, TestData.Now);

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

    /// <summary>
    /// Moves the clock on once the service waits for it, so the time it waits from isn't moved under it.
    /// </summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!_clock.IsWaiting)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }

        _time.Advance(by);
    }

    private async Task WaitForNoticesAsync(int notices)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (_notifications.Shown.Count < notices)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    private async Task RunAsync(Func<Task> test)
    {
        using UpdateCheckService service = new(_checker, _settings, _library, _notifications, _clock, _logger);
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

        /// <summary>
        /// The new updates each check finds.
        /// </summary>
        public IReadOnlyList<LibraryUpdate> Found { get; set; } = [];

        /// <summary>
        /// Runs during each check, with its number, from 1.
        /// </summary>
        public Action<int>? OnCheck { get; set; }

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
        {
            OnCheck?.Invoke(Checks + 1);
            Interlocked.Increment(ref _checks);
            return Failure is { } failure ? Task.FromException<UpdateCheck>(failure) : Task.FromResult(new UpdateCheck(0, 0, Found, []));
        }
    }

    /// <summary>
    /// The fake clock, which the test can also move back, which a fake clock can't, and which knows whether anything waits
    /// on one of its timers.
    /// </summary>
    private sealed class TestClock(FakeTimeProvider timers) : TimeProvider
    {
        private long _shift;
        private int _waiting;

        public override long TimestampFrequency => timers.TimestampFrequency;

        /// <summary>
        /// Whether a timer is set that hasn't been disposed of, as a delay's is once it's over.
        /// </summary>
        public bool IsWaiting => Volatile.Read(ref _waiting) > 0;

        public void MoveBack(TimeSpan by) => Interlocked.Add(ref _shift, -by.Ticks);

        public override DateTimeOffset GetUtcNow() => timers.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _shift));

        public override long GetTimestamp() => timers.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Counted once the fake clock has it, so a move of the clock that sees it is a move the timer sees too.
            ITimer timer = timers.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _waiting);
            return new WatchedTimer(timer, () => Interlocked.Decrement(ref _waiting));
        }

        private sealed class WatchedTimer(ITimer timer, Action disposed) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    disposed();
                }

                timer.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
