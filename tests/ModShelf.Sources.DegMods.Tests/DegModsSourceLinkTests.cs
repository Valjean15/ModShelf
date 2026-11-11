using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

public sealed class DegModsSourceLinkTests : IDisposable
{
    private static readonly Uri Site = DegModsOptions.DefaultSiteAddress;

    private readonly RecordedDegMods _degMods = new();
    private readonly ServiceProvider _services;

    public DegModsSourceLinkTests()
    {
        _services = _degMods.BuildServices();
    }

    [Fact]
    public async Task AModsPageIsItsMainFileUnderItsGame()
    {
        RecognizedLink? link = await Source().RecognizeAsync(Page(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers), TestContext.Current.CancellationToken);

        Assert.NotNull(link);
        ModFileRef expected = new(
            new ModRef(new GameRef(new SourceId("degmods"), "fallout: new vegas"), $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}"),
            RecordedDegMods.NewspapersFile[..16]);
        Assert.Equal(expected, link.File);
        Assert.Equal("Better Newspapers for New Vegas Bounties II-96533-1-0-1771837702.zip", link.SuggestedFileName);
        Assert.False(link.IsCatchAll);
        Assert.False(link.IsHandoff);
    }

    [Fact]
    public async Task AnOlderListingsPageIsTheModAsItsNewEventSaysIt()
    {
        Uri old = DegModsLink.ModPage(Site, DegModsLink.OldModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Nipton);

        RecognizedLink? link = await Source().RecognizeAsync(old, TestContext.Current.CancellationToken);

        // The new event's first file, which the older listing hadn't.
        Assert.Equal(RecordedDegMods.NiptonFile[..16], link?.File.Id);
    }

    [Fact]
    public async Task AModsAddressIsItsMainFile()
    {
        RecognizedLink? link = await Source().RecognizeAsync(
            new Uri($"nostr:{Nip19.Naddr(DegModsLink.OldModKind, RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration)}"), TestContext.Current.CancellationToken);

        Assert.Equal(("starfield", "wa4qcn.7z"), (link?.File.Mod.Game.Key, link?.SuggestedFileName));
    }

    [Fact]
    public async Task ModShelfsLinkToAFileIsThatFile()
    {
        Uri second = DegModsLink.FilePage(Site, DegModsLink.ModKind, RecordedDegMods.WifiAuthor, RecordedDegMods.RejectWifi, "8fd6e5a7d1b79081");

        RecognizedLink? link = await Source().RecognizeAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(("8fd6e5a7d1b79081", "reframework.zip"), (link?.File.Id, link?.SuggestedFileName));
    }

    [Fact]
    public async Task RefusesAFileTheModNoLongerLists()
    {
        Uri gone = DegModsLink.FilePage(Site, DegModsLink.ModKind, RecordedDegMods.WifiAuthor, RecordedDegMods.RejectWifi, "0123456789abcdef");

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => Source().RecognizeAsync(gone, TestContext.Current.CancellationToken));

        Assert.Contains("no longer lists that file", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAModWithoutFiles()
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("empty", "Nothing yet", "Game", 100, []));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => Source().RecognizeAsync(Page(author.PublicKey, "empty"), TestContext.Current.CancellationToken));

        Assert.Contains("no files", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAModItsModerationHides()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Source().RecognizeAsync(Page(RecordedDegMods.TestPostAuthor, RecordedDegMods.BlockedPost), TestContext.Current.CancellationToken));

        Assert.Contains("moderation hides", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModThatIsntThereIsNotFound()
    {
        HttpRequestException missing = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source().RecognizeAsync(Page(RecordedDegMods.FalloutAuthor, "no-such-mod"), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/newvegas/mods/1")]
    [InlineData("https://files.catbox.moe/wa4qcn.7z")]
    [InlineData("nostr:npub17jl3ldd6305rnacvwvchx03snauqsg4nz8mruq0emj9thdpglr2sst825x")]
    public async Task LeavesOtherLinksAlone(string link)
    {
        Assert.Null(await Source().RecognizeAsync(new Uri(link), TestContext.Current.CancellationToken));
        Assert.Empty(_degMods.Brs.Filters);
    }

    [Fact]
    public async Task RefusesDegModsLinksToSomethingElseWithoutAskingTheRelays()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source().RecognizeAsync(new Uri("https://degmods.com/game/Starfield"), TestContext.Current.CancellationToken));
        Assert.Empty(_degMods.Brs.Filters);
    }

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    private static Uri Page(string author, string d) => DegModsLink.ModPage(Site, DegModsLink.ModKind, author, d);

    private DegModsSource Source() => _services.GetServices<IModSource>().OfType<DegModsSource>().Single();
}
