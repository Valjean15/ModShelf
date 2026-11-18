using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Updates;

/// <summary>
/// Mods that leave their site: marked gone, not failed, when the site won't list their files and then says they're
/// hidden or removed; asked about again weekly; and back when their site lists their files again. Mods 1 and 2 each had
/// version 10 replaced by 11; the library has version 10 of each.
/// </summary>
public sealed class UpdateCheckerGoneTests : IDisposable
{
    private static readonly HttpRequestException NotFound = new("Not found.", null, HttpStatusCode.NotFound);

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly FakeModSite _site = new();
    private readonly FakeModSiteWithFeed _feed;
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemoryLibraryHistory _history = new();
    private readonly LibraryChanges _changes = new();
    private readonly SettingsService _settings;
    private readonly RecordingLogger<UpdateChecker> _logger = new();

    public UpdateCheckerGoneTests()
    {
        _feed = new FakeModSiteWithFeed(_site, _time);
        _settings = new SettingsService(new InMemorySettingsStore(), new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        foreach (string mod in new[] { "1", "2" })
        {
            _site.AddMod(mod, $"Mod {mod}");
            _site.AddFile(mod, "10", "10.7z", "Version 10", "10", replacedBy: "11", uploadedAt: TestData.Now.AddDays(-60));
            _site.AddFile(mod, "11", "11.7z", "Version 11", "11", uploadedAt: TestData.Now.AddDays(-2));
            _library.Add(LibraryMod.Watch(Mod(mod), $"Mod {mod}", null, null, TestData.Now.AddDays(-90))
                .WithFile(new LibraryFile(File(mod, "10"), "Version 10", "10", ModFileCategory.Main, TestData.Now.AddDays(-60), $"mods/{mod}/10.7z", 100, TestData.Now.AddDays(-50))));
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task AModItsSiteHidesIsGoneRatherThanFailed()
    {
        _site.Failures[nameof(FakeModSite.GetFilesAsync)] = new HttpRequestException("This mod is hidden.", null, HttpStatusCode.Forbidden);
        _site.Hidden["1"] = true;

        UpdateCheck check = await CheckAsync(_site);

        Assert.Empty(check.Failures);
        Assert.Equal((2, 3), (check.Checked, check.Requests));
        LibraryMod gone = Assert.Single(check.Gone);
        Assert.Equal((Mod("1"), TestData.Now), (gone.Mod, gone.GoneSince));
        Assert.Empty(Stored("1").Updates);
        Assert.Equal(TestData.Now, Stored("1").CheckedAt);
        Assert.Equal(["11"], check.Found.Select(update => update.File.Id));
        LibraryEvent left = Assert.Single(_history.Events);
        Assert.Equal((LibraryEventKind.LeftSite, Mod("1"), "Mod 1", (ModFileRef?)null), (left.Kind, left.Mod, left.ModName, left.File));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Mod 1 of skyrimspecialedition is gone from fake-mods", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AModItsSiteNoLongerKnowsIsGoneToo()
    {
        _site.ModFailures["1"] = NotFound;

        UpdateCheck check = await CheckAsync(_site);

        Assert.Equal(Mod("1"), Assert.Single(check.Gone).Mod);
        Assert.Empty(check.Failures);
    }

    [Fact]
    public async Task AFailureThatDoesntMeanGoneIsStillAFailure()
    {
        _site.Failures[nameof(FakeModSite.GetFilesAsync)] = NotFound;

        UpdateCheck described = await CheckAsync(_site);
        _site.Failures[nameof(FakeModSite.GetFilesAsync)] = new HttpRequestException("Busy.", null, HttpStatusCode.InternalServerError);
        int asked = _site.Asked.Count;
        UpdateCheck busy = await CheckAsync(_site);

        // Asked, the site still describes the mod as available; a server's error isn't a reason to ask.
        Assert.Empty(described.Gone);
        Assert.Equal("Not found.", Assert.Single(described.Failures).Reason);
        Assert.Empty(busy.Gone);
        Assert.Equal("Busy.", Assert.Single(busy.Failures).Reason);
        Assert.DoesNotContain(nameof(FakeModSite.GetModAsync), _site.Asked.Skip(asked));
        Assert.All(_library.Mods, mod => Assert.False(mod.IsGone));
    }

    [Fact]
    public async Task AGoneModIsAskedAboutAgainOnlyWeeklyAndStaysGoneSinceItLeft()
    {
        _site.ModFailures["1"] = NotFound;
        await CheckAsync(_feed);

        _time.Advance(TimeSpan.FromDays(1));
        int asked = _site.AskedFor.Count;
        UpdateCheck soon = await CheckAsync(_feed);
        string[] soonAsked = [.. _site.AskedFor.Skip(asked)];
        _time.Advance(TimeSpan.FromDays(6));
        asked = _site.AskedFor.Count;
        UpdateCheck weekly = await CheckAsync(_feed);

        Assert.Equal(1, soon.Checked);
        Assert.DoesNotContain(soonAsked, question => question.EndsWith(" 1", StringComparison.Ordinal));
        Assert.Equal(2, weekly.Checked);
        Assert.Contains("GetFilesAsync 1", _site.AskedFor.Skip(asked));
        Assert.Empty(weekly.Gone);
        Assert.Equal((TestData.Now, TestData.Now.AddDays(7)), (Stored("1").GoneSince, Stored("1").CheckedAt));
        Assert.Single(_history.Events);
    }

    [Fact]
    public async Task AModBackOnItsSiteLosesTheMarkAndItsUpdatesAreFoundAgain()
    {
        _site.ModFailures["1"] = NotFound;
        await CheckAsync(_site);
        _site.ModFailures.TryRemove("1", out _);
        _time.Advance(UpdateChecker.GoneRecheck);

        UpdateCheck check = await CheckAsync(_site);

        LibraryMod back = Assert.Single(check.Back);
        Assert.Equal((Mod("1"), false), (back.Mod, back.IsGone));
        Assert.Equal("11", Assert.Single(Stored("1").Updates).File.Id);
        Assert.Equal([LibraryEventKind.LeftSite, LibraryEventKind.BackOnSite], _history.Events.OrderBy(happened => happened.At).Select(happened => happened.Kind));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Mod 1 of skyrimspecialedition is back on fake-mods", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnwatchedGoneModIsntChecked()
    {
        _site.ModFailures["1"] = NotFound;
        await CheckAsync(_site);
        await _library.UpdateAsync(Mod("1"), stored => stored!.Unwatched(), Cancellation);
        _time.Advance(UpdateChecker.GoneRecheck);
        int asked = _site.AskedFor.Count;

        UpdateCheck check = await CheckAsync(_site);

        Assert.Equal(1, check.Checked);
        Assert.DoesNotContain("GetFilesAsync 1", _site.AskedFor.Skip(asked));
        Assert.True(Stored("1").IsGone);
    }

    private LibraryMod Stored(string mod) => _library.Mods.Single(stored => stored.Mod.Id == mod);

    private async Task<UpdateCheck> CheckAsync(IModSource source)
    {
        using UpdateChecker checker = new(new SourceRegistry([source]), _library, _settings, _changes, _history.History(_time), _time, _logger);
        return await checker.CheckAsync(Cancellation);
    }
}
