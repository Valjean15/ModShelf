using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;
using static ModShelf.Application.Tests.Fakes.FakeModSiteWithFeed;

namespace ModShelf.Application.Tests.Updates;

/// <summary>
/// Checks against a site whose mods 1 and 2 each had version 10 replaced by 11, and whose mod 3 has nothing new. The
/// library has version 10 of each, and of mod 3 its only file, 30.
/// </summary>
public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly GameRef Fallout = new(FakeModSite.SourceId, "fallout4");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly FakeModSite _site = new();
    private readonly FakeModSiteWithFeed _feed;
    private readonly InMemoryLibraryRepository _library = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingLogger<UpdateChecker> _logger = new();

    public UpdateCheckerTests()
    {
        _feed = new FakeModSiteWithFeed(_site, _time);
        foreach (string mod in new[] { "1", "2" })
        {
            _site.AddMod(mod, $"Mod {mod}");
            _site.AddFile(mod, "10", "10.7z", "Version 10", "10", replacedBy: "11", uploadedAt: TestData.Now.AddDays(-60));
            _site.AddFile(mod, "11", "11.7z", "Version 11", "11", uploadedAt: TestData.Now.AddDays(-2));
            _library.Add(Watched(mod).WithFile(Stored(mod, "10")));
        }

        _site.AddMod("3", "Mod 3");
        _site.AddFile("3", "30", "30.7z", uploadedAt: TestData.Now.AddDays(-60));
        _library.Add(Watched("3").WithFile(Stored("3", "30")));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _feed.Site.Hold?.TrySetResult();

    [Fact]
    public async Task AFirstCheckAsksAboutEachModAndKeepsWhatItFinds()
    {
        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((3, 3), (check.Checked, check.Requests));
        Assert.Equal(["1 11", "2 11"], check.Found.Select(update => $"{update.File.Mod.Id} {update.File.Id}"));
        Assert.Empty(check.Failures);
        Assert.Empty(_feed.Questions);
        Assert.Equal("10", Assert.Single(Mod("1").Updates).Replaces?.Id);
        Assert.All(_library.Mods, mod => Assert.Equal(TestData.Now, mod.CheckedAt));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Checked 3 watched mods for updates with 3 requests: 2 new updates, 0 failures", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EachModCheckedTellsWhoWatchesTheLibrary()
    {
        using WatchedChanges watched = new(_changes);

        await CheckAsync(_feed);

        IReadOnlyList<LibraryChange> changes = await watched.StopAsync();
        Assert.Equal(["1", "2", "3"], changes.Select(change => change.Mod.Id));
        Assert.All(changes, change => Assert.Equal((null, TestData.Now), (change.Before!.CheckedAt, change.After!.CheckedAt)));
        Assert.Equal([1, 1, 0], changes.Select(change => change.After!.Updates.Count));
    }

    [Fact]
    public async Task ModsTheFeedSaysAreUnchangedAreToldCheckedToo()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _time.Advance(TimeSpan.FromHours(1));
        _feed.Changes.Add(Changed("1", TestData.Now.AddMinutes(-30), TestData.Now.AddMinutes(-30)));
        using WatchedChanges watched = new(_changes);

        await CheckAsync(_feed);

        IReadOnlyList<LibraryChange> changes = await watched.StopAsync();
        Assert.Equal(["1", "2", "3"], changes.Select(change => change.Mod.Id).Order());
        Assert.All(changes, change => Assert.Equal(TestData.Now.AddHours(1), change.After!.CheckedAt));
    }

    [Fact]
    public async Task AModWhoseLibrarySideChangedIsAskedAboutWhateverTheFeedSays()
    {
        // Watched again, say: its updates went when it was unwatched, and the site's files haven't changed since, so the
        // feed alone would leave it without them for good.
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        await _library.UpdateAsync(Mod("1").Mod, stored => stored!.Unwatched()!.Watched(), Cancellation);
        _time.Advance(TimeSpan.FromHours(1));

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal(["GetFilesAsync 1"], _site.AskedFor);
        Assert.Equal((3, 2), (check.Checked, check.Requests));
        Assert.Equal("11", Assert.Single(Mod("1").Updates).File.Id);
        Assert.False(Mod("1").NeedsFullCheck);
    }

    [Fact]
    public async Task ALaterCheckAsksTheFeedOnceThenOnlyAboutModsWhoseFilesChanged()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _time.Advance(TimeSpan.FromHours(1));
        _feed.Changes.AddRange(
        [
            Changed("1", TestData.Now.AddMinutes(-30), TestData.Now.AddMinutes(-30)),
            Changed("2", TestData.Now.AddMinutes(-20), TestData.Now.AddDays(-60)),
        ]);

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((3, 2), (check.Checked, check.Requests));
        Assert.Equal((Mod("1").Mod.Game, TestData.Now.AddHours(-2) - UpdateChecker.Margin), Assert.Single(_feed.Questions));
        Assert.Equal(["GetFilesAsync 1"], _site.AskedFor);
        Assert.All(_library.Mods, mod => Assert.Equal(TestData.Now.AddHours(1), mod.CheckedAt));
    }

    [Fact]
    public async Task AFeedThatDoesntSayWhenFilesChangedMeansAsking()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _feed.Changes.Add(Changed("3", TestData.Now.AddMinutes(-30), null));

        await CheckAsync(_feed);

        Assert.Equal(["GetFilesAsync 3"], _site.AskedFor);
    }

    [Fact]
    public async Task FilesChangedJustBeforeTheLastCheckStillCount()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _feed.Changes.Add(Changed("1", TestData.Now.AddHours(-2).AddMinutes(-9), TestData.Now.AddHours(-2).AddMinutes(-9)));
        _feed.Changes.Add(Changed("2", TestData.Now.AddHours(-2).AddMinutes(-9), TestData.Now.AddHours(-2).AddMinutes(-11)));

        await CheckAsync(_feed);

        Assert.Equal(["GetFilesAsync 1"], _site.AskedFor);
    }

    [Fact]
    public async Task ModsCheckedLongerAgoThanTheFeedReachesAreAskedAboutOnTheirOwn()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _library.Add(Watched("4", checkedAt: TestData.Now.AddDays(-28) + UpdateChecker.Margin + TimeSpan.FromSeconds(30)));
        _site.AddMod("4", "Mod 4");

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal(4, check.Checked);
        Assert.Equal(TestData.Now.AddHours(-2) - UpdateChecker.Margin, Assert.Single(_feed.Questions).Since);
        Assert.Equal(["GetFilesAsync 4"], _site.AskedFor);
    }

    [Fact]
    public async Task TheFeedIsAskedSinceTheOldestCheckItCovers()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _library.Add(Watched("4", checkedAt: TestData.Now.AddHours(-5)));
        _site.AddMod("4", "Mod 4");

        await CheckAsync(_feed);

        Assert.Equal(TestData.Now.AddHours(-5) - UpdateChecker.Margin, Assert.Single(_feed.Questions).Since);
        Assert.Empty(_site.AskedFor);
    }

    [Fact]
    public async Task ASiteWithoutAFeedIsAskedAboutEachMod()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));

        UpdateCheck check = await CheckAsync(_site);

        Assert.Equal((3, 3), (check.Checked, check.Requests));
    }

    [Fact]
    public async Task AFeedIsAskedOncePerGame()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _library.Add(LibraryMod.Watch(new ModRef(Fallout, "5"), "Mod 5", null, null, TestData.Now.AddDays(-9)).Checked(TestData.Now.AddHours(-1)));

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal([Mod("1").Mod.Game, Fallout], _feed.Questions.Select(question => question.Game));
        Assert.Equal((4, 2), (check.Checked, check.Requests));
    }

    [Fact]
    public async Task OnlyWatchedModsAreChecked()
    {
        await _library.UpdateAsync(Mod("2").Mod, mod => mod!.Unwatched(), Cancellation);

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal(2, check.Checked);
        Assert.Null(Mod("2").CheckedAt);
    }

    [Fact]
    public async Task AModThatCantBeCheckedStaysAsItWas()
    {
        _site.ModFailures["1"] = new HttpRequestException("Not found.", null, HttpStatusCode.NotFound);

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal(2, check.Checked);
        UpdateCheckFailure failure = Assert.Single(check.Failures);
        Assert.Equal((Mod("1").Mod.Game, Mod("1").Mod, "Not found."), (failure.Game, failure.Mod, failure.Reason));
        Assert.Null(Mod("1").CheckedAt);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't check mod 1 of skyrimspecialedition on fake-mods", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGameWhoseFeedFailsStaysAsItWas()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _feed.Failure = new HttpRequestException("Unreachable.");

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((0, 1), (check.Checked, check.Requests));
        Assert.Equal((Mod("1").Mod.Game, (ModRef?)null), (Assert.Single(check.Failures).Game, check.Failures[0].Mod));
        Assert.All(_library.Mods, mod => Assert.Equal(TestData.Now.AddHours(-2), mod.CheckedAt));
    }

    [Fact]
    public async Task ARefusedAccountStopsAskingThatSite()
    {
        _library.Add(LibraryMod.Watch(new ModRef(Fallout, "5"), "Mod 5", null, null, TestData.Now.AddDays(-9)));
        _site.ModFailures["1"] = new AuthenticationFailedException("The account was refused.");

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((0, 1), (check.Checked, check.Requests));
        Assert.Equal(["The account was refused.", "Not checked: fake-mods refused an earlier question in this check. The account was refused."], check.Failures.Select(failure => failure.Reason));
        Assert.Equal(Fallout, check.Failures[1].Game);
    }

    [Fact]
    public async Task BeingAskedToSlowDownStopsAskingThatSite()
    {
        _site.ModFailures["2"] = new HttpRequestException("Slow down.", null, HttpStatusCode.TooManyRequests);

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((1, 2), (check.Checked, check.Requests));
        Assert.Equal(["GetFilesAsync 1", "GetFilesAsync 2"], _site.AskedFor);
    }

    [Fact]
    public async Task ASiteThatCantBeReachedStopsBeingAsked()
    {
        // Offline, say: each question would wait out its retries and timeouts in turn, minutes a mod.
        _library.Add(LibraryMod.Watch(new ModRef(Fallout, "5"), "Mod 5", null, null, TestData.Now.AddDays(-9)));
        _site.ModFailures["1"] = new HttpRequestException("No such host is known.");

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((0, 1), (check.Checked, check.Requests));
        Assert.Equal(["GetFilesAsync 1"], _site.AskedFor);
        Assert.Equal(
            ["No such host is known.", "Not checked: fake-mods couldn't be reached earlier in this check. No such host is known."],
            check.Failures.Select(failure => failure.Reason));
    }

    [Fact]
    public async Task AnAnswerWithAStatusDoesntStopTheSite()
    {
        // The site answered: it's there, and the next mod may well check.
        _site.ModFailures["1"] = new HttpRequestException("Server error.", null, HttpStatusCode.InternalServerError);

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((2, 3), (check.Checked, check.Requests));
    }

    [Fact]
    public async Task ASiteThatCantDescribeItsModsCantBeChecked()
    {
        UpdateCheck check = await CheckAsync(ScriptedLinkRecognizer.Never(FakeModSite.SourceId));

        Assert.Equal(0, check.Checked);
        Assert.Equal("fake-mods can't describe its mods, so they can't be checked for updates.", Assert.Single(check.Failures).Reason);
    }

    [Fact]
    public async Task UpdatesFoundBeforeArentNews()
    {
        await CheckAsync(_feed);
        _time.Advance(TimeSpan.FromDays(40));

        UpdateCheck again = await CheckAsync(_feed);

        Assert.Empty(again.Found);
        Assert.Equal(TestData.Now, Assert.Single(Mod("1").Updates).FoundAt);
        Assert.Equal(TestData.Now.AddDays(40), Mod("1").CheckedAt);
    }

    [Fact]
    public async Task ANewerUpdateReplacesTheOneFoundBefore()
    {
        await CheckAsync(_feed);
        _site.RemoveFile("1", "11");
        _site.AddFile("1", "11", "11.7z", "Version 11", "11", replacedBy: "12", uploadedAt: TestData.Now.AddDays(-2));
        _site.AddFile("1", "12", "12.7z", "Version 12", "12", uploadedAt: TestData.Now.AddHours(1));
        _time.Advance(TimeSpan.FromHours(2));

        UpdateCheck again = await CheckAsync(_site);

        Assert.Equal("12", Assert.Single(again.Found).File.Id);
        Assert.Equal("12", Assert.Single(Mod("1").Updates).File.Id);
    }

    [Fact]
    public async Task ARefusedFeedStopsAskingThatSite()
    {
        await CheckedAtAsync(TestData.Now.AddHours(-2));
        _library.Add(LibraryMod.Watch(new ModRef(Fallout, "5"), "Mod 5", null, null, TestData.Now.AddDays(-9)).Checked(TestData.Now.AddHours(-1)));
        _feed.Failure = new AuthenticationFailedException("The account was refused.");

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Equal((0, 1), (check.Checked, check.Requests));
        Assert.Single(_feed.Questions);
        Assert.Equal(["The account was refused.", "Not checked: fake-mods refused an earlier question in this check. The account was refused."], check.Failures.Select(failure => failure.Reason));
    }

    [Fact]
    public async Task AWatchedModWithoutFilesGetsTheMainFilesSinceItsLastCheck()
    {
        _library.Add(Watched("6", checkedAt: TestData.Now.AddHours(-2)));
        _site.AddMod("6", "Mod 6");
        _site.AddFile("6", "60", "60.7z", uploadedAt: TestData.Now.AddDays(-1));
        _site.AddFile("6", "61", "61.7z", uploadedAt: TestData.Now.AddHours(-2) - UpdateChecker.Margin + TimeSpan.FromMinutes(1));
        _site.AddFile("6", "62", "62.7z", uploadedAt: TestData.Now.AddHours(-1));

        await CheckAsync(_site);

        Assert.Equal(["62", "61"], Mod("6").Updates.Select(update => update.File.Id));
        Assert.All(Mod("6").Updates, update => Assert.Null(update.Replaces));
    }

    [Fact]
    public async Task ModsCheckedLaterThanNowAreAskedAboutOnTheirOwn()
    {
        // The clock has moved back since the last check, so when that was isn't known.
        await CheckedAtAsync(TestData.Now.AddDays(30));

        UpdateCheck check = await CheckAsync(_feed);

        Assert.Empty(_feed.Questions);
        Assert.Equal(["GetFilesAsync 1", "GetFilesAsync 2", "GetFilesAsync 3"], _site.AskedFor);
        Assert.Equal(["1 11", "2 11"], check.Found.Select(update => $"{update.File.Mod.Id} {update.File.Id}"));
        Assert.All(_library.Mods, mod => Assert.Equal(TestData.Now, mod.CheckedAt));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-30)]
    public async Task AWatchedModWithoutFilesCheckedLaterThanNowGetsTheMainFilesSinceItWasAdded(int addedDaysAgo)
    {
        DateTimeOffset added = TestData.Now.AddDays(-addedDaysAgo);
        _library.Add(LibraryMod.Watch(FakeModSite.Mod("6"), "Mod 6", null, null, added).Checked(TestData.Now.AddDays(30)));
        _site.AddMod("6", "Mod 6");
        _site.AddFile("6", "59", "59.7z", uploadedAt: TestData.Now.AddDays(-100));
        _site.AddFile("6", "60", "60.7z", uploadedAt: TestData.Now.AddDays(-1));
        _site.AddFile("6", "61", "61.7z", uploadedAt: TestData.Now.AddMinutes(-5));

        await CheckAsync(_site);

        string[] expected = added > TestData.Now ? ["61"] : ["61", "60"];
        Assert.Equal(expected, Mod("6").Updates.Select(update => update.File.Id));
    }

    [Fact]
    public async Task AModUnwatchedDuringTheCheckStaysUnwatched()
    {
        _site.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using UpdateChecker checker = Checker(_site);

        Task<UpdateCheck> checking = checker.CheckAsync(Cancellation);
        await WaitUntilAsync(() => _site.AskedFor.Count == 1);
        await _library.UpdateAsync(FakeModSite.Mod("1"), stored => stored!.Unwatched(), Cancellation);
        _site.Hold.SetResult();
        UpdateCheck check = await checking;

        Assert.Equal(2, check.Checked);
        Assert.Equal(["2 11"], check.Found.Select(update => $"{update.File.Mod.Id} {update.File.Id}"));
        Assert.False(Mod("1").IsWatched);
        Assert.Null(Mod("1").CheckedAt);
    }

    [Fact]
    public async Task OneCheckRunsAtATime()
    {
        _site.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using UpdateChecker checker = Checker(_site);

        Task<UpdateCheck> first = checker.CheckAsync(Cancellation);
        Task<UpdateCheck> second = checker.CheckAsync(Cancellation);
        await WaitUntilAsync(() => _site.AskedFor.Count == 1);
        await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        int askedWhileHeld = _site.AskedFor.Count;
        _site.Hold.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, askedWhileHeld);
        Assert.Equal(6, _site.AskedFor.Count);
    }

    private static LibraryMod Watched(string mod, DateTimeOffset? checkedAt = null)
    {
        LibraryMod watched = LibraryMod.Watch(FakeModSite.Mod(mod), $"Mod {mod}", null, null, TestData.Now.AddDays(-90));
        return checkedAt is { } at ? watched.Checked(at) : watched;
    }

    private static LibraryFile Stored(string mod, string file)
        => new(File(mod, file), $"Version {file}", file, ModFileCategory.Main, TestData.Now.AddDays(-60), $"mods/{mod}/{file}.7z", 100, TestData.Now.AddDays(-50));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    private LibraryMod Mod(string mod) => _library.Mods.Single(stored => stored.Mod.Id == mod);

    private async Task CheckedAtAsync(DateTimeOffset at)
    {
        foreach (LibraryMod mod in _library.Mods)
        {
            await _library.UpdateAsync(mod.Mod, stored => stored!.Checked(at), Cancellation);
        }
    }

    private async Task<UpdateCheck> CheckAsync(IModSource source)
    {
        using UpdateChecker checker = Checker(source);
        return await checker.CheckAsync(Cancellation);
    }

    private UpdateChecker Checker(IModSource source) => new(new SourceRegistry([source]), _library, _changes, _time, _logger);
}
