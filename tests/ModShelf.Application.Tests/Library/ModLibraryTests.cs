using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Library;

public sealed class ModLibraryTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly FakeCatalogSource _site = new(TestData.Nexus);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ModRef SkyUi => _site.LinkedFile.Mod;

    [Fact]
    public async Task WatchesTheModOfALinkWithItsDetailsFromItsSite()
    {
        LibraryMod watched = await Library(_site).WatchAsync(Link, Cancellation);

        Assert.Equal((SkyUi, "SkyUI", true, TestData.Now), (watched.Mod, watched.Name, watched.IsWatched, watched.AddedAt));
        Assert.Empty(watched.Files);
        Assert.Same(watched, Assert.Single(_repository.Mods));
        Assert.Equal(["RecognizeAsync", "GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task WatchingAgainKeepsTheFilesAndUpdatesTheDetails()
    {
        LibraryFile file = File("35407");
        _repository.Add(LibraryMod.Restore(SkyUi, "SkyUI (old name)", "someone", null, isWatched: false, TestData.Now.AddDays(-9), null, [file]));
        _time.Advance(TimeSpan.FromHours(1));

        LibraryMod watched = await Library(_site).WatchAsync(Link, Cancellation);

        Assert.Equal(("SkyUI", (string?)null, true), (watched.Name, watched.Author, watched.IsWatched));
        Assert.Equal(TestData.Now.AddDays(-9), watched.AddedAt);
        Assert.Same(file, Assert.Single(watched.Files));
    }

    [Fact]
    public async Task UnwatchingAModWithoutFilesTakesItOutOfTheLibrary()
    {
        ModLibrary library = Library(_site);
        LibraryMod watched = await library.WatchAsync(Link, Cancellation);

        LibraryChange change = await library.UnwatchAsync(Link, Cancellation);

        Assert.Equal(new LibraryChange(SkyUi, watched, null), change);
        Assert.Empty(await library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task UnwatchingAModWithFilesKeepsThem()
    {
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now).WithFile(File("35407")));

        LibraryChange change = await Library(_site).UnwatchAsync(Link, Cancellation);

        Assert.False(change.After!.IsWatched);
        Assert.Single(change.After.Files);
        Assert.Same(change.After, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task UnwatchingAModNotInTheLibraryChangesNothing()
    {
        LibraryChange change = await Library(_site).UnwatchAsync(Link, Cancellation);

        Assert.Equal(new LibraryChange(SkyUi, null, null), change);
        Assert.DoesNotContain("GetModAsync", _site.Asked);
    }

    [Fact]
    public async Task FindsAndListsWhatTheLibraryHas()
    {
        LibraryMod mod = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now);
        _repository.Add(mod);
        ModLibrary library = Library(_site);

        Assert.Same(mod, await library.FindAsync(SkyUi, Cancellation));
        Assert.Null(await library.FindAsync(new ModRef(SkyUi.Game, "3863"), Cancellation));
        Assert.Equal([mod], await library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => Library(ScriptedLinkRecognizer.Never(TestData.Nexus)).WatchAsync(Link, Cancellation));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task OnlyWatchesModsOnSitesThatDescribeThem()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);
        ModLibrary library = Library(direct);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => library.WatchAsync(Link, Cancellation));
        LibraryChange unwatched = await library.UnwatchAsync(Link, Cancellation);

        Assert.Contains("can watch mods", refused.Message, StringComparison.Ordinal);
        Assert.Null(unwatched.Before);
        Assert.Empty(_repository.Mods);
    }

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "GetModAsync", new AuthenticationFailedException("The site refused the account.") },
        { "GetModAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSiteRefusesAndStoresNothing(string method, Exception failure)
    {
        _site.Failures[method] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => Library(_site).WatchAsync(Link, Cancellation));

        Assert.Same(failure, thrown);
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        Uri relative = new("skyrimspecialedition/mods/12604", UriKind.Relative);
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<ArgumentException>(() => library.WatchAsync(relative, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => library.UnwatchAsync(relative, Cancellation));
        Assert.Empty(_site.Asked);
    }

    private static LibraryFile File(string id)
        => new(new ModFileRef(TestData.File(TestData.Nexus, id).Mod, id), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now, "SkyUI_5_2_SE.7z", 1, TestData.Now);

    private ModLibrary Library(params IModSource[] sources) => new(new SourceRegistry(sources), _repository, _time);
}
