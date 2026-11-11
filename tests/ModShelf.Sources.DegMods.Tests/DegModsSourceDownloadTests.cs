using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

public sealed class DegModsSourceDownloadTests : IDisposable
{
    private static readonly SourceId DegMods = new("degmods");
    private static readonly Uri Site = DegModsOptions.DefaultSiteAddress;

    private readonly RecordedDegMods _degMods = new();
    private readonly ServiceProvider _services;

    public DegModsSourceDownloadTests()
    {
        _services = _degMods.BuildServices();
    }

    [Fact]
    public async Task TakesAFileOnDegModsServerFromAnotherThatHasItWithItsSha256()
    {
        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, RecordedDegMods.NewspapersFile[..16]);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(result);
        Assert.Equal($"https://blossom.primal.net/{RecordedDegMods.NewspapersFile}", ticket.Url.AbsoluteUri);
        Assert.Equal(new FileHash(FileHashAlgorithm.Sha256, RecordedDegMods.NewspapersFile), ticket.ExpectedHash);
        Assert.Equal([$"HEAD blossom.primal.net/{RecordedDegMods.NewspapersFile}"], _degMods.Asked);
    }

    [Fact]
    public async Task OpensTheModsPageForAFileOnlyDegModsServerHasAskingEveryOtherFirst()
    {
        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Nipton, RecordedDegMods.NiptonFile[..16]);

        HandoffRequired handoff = Assert.IsType<HandoffRequired>(result);
        Assert.Equal(DegModsLink.ModPage(Site, DegModsLink.ModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Nipton), handoff.PageUrl);
        Assert.Equal(DegModsOptions.DefaultFileServers.Select(server => $"HEAD {server.Host}/{RecordedDegMods.NiptonFile}"), _degMods.Asked);
        Assert.DoesNotContain(_degMods.Asked, asked => asked.Contains("degmods.com", StringComparison.Ordinal));
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("only on DEG Mods' own server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TreatsDegModsOldServerAsItsOwn()
    {
        // Smook's Police Gear Pack's first file is on bs.degmods.com, which no other server has.
        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.PoliceGear, "57e4fb2c82c6f67c");

        Assert.IsType<HandoffRequired>(result);
        Assert.DoesNotContain(_degMods.Asked, asked => asked.Contains("degmods.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NeverAsksDegModsOwnServerEvenAmongTheFileServers()
    {
        using ServiceProvider services = _degMods.BuildServices(options => options with
        {
            FileServers = [new Uri("https://brs.degmods.com/"), new Uri("https://blossom.primal.net/")],
        });
        DegModsSource source = services.GetServices<IModSource>().OfType<DegModsSource>().Single();

        ResolveResult result = await source.ResolveAsync(
            new ModFileRef(new ModRef(new GameRef(DegMods, "fallout: new vegas"), $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Nipton}"), RecordedDegMods.NiptonFile[..16]),
            handoff: null,
            TestContext.Current.CancellationToken);

        Assert.IsType<HandoffRequired>(result);
        Assert.Equal([$"HEAD blossom.primal.net/{RecordedDegMods.NiptonFile}"], _degMods.Asked);
    }

    [Fact]
    public async Task AsksAnotherServersFileOfItsOwnLinkFirst()
    {
        // Better Newspapers' third file is on blossom.primal.net itself.
        string sha256 = (await Files(RecordedDegMods.Newspapers))[2].Hashes[0].Value;
        Uri own = new($"https://blossom.primal.net/{sha256}.zip");
        _degMods.Answer("HEAD", own, 200);

        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, sha256[..16]);

        Assert.Equal(own, Assert.IsType<DownloadTicket>(result).Url);
        Assert.Equal([$"HEAD blossom.primal.net/{sha256}.zip"], _degMods.Asked);
    }

    [Fact]
    public async Task AsksTheOtherServersWhenAFilesOwnServerLacksIt()
    {
        string sha256 = (await Files(RecordedDegMods.Newspapers))[2].Hashes[0].Value;
        _degMods.Answer("HEAD", new Uri($"https://nostr.download/{sha256}"), 200);

        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, sha256[..16]);

        Assert.Equal($"https://nostr.download/{sha256}", Assert.IsType<DownloadTicket>(result).Url.AbsoluteUri);

        // Its own server once, under the name its link gives the file.
        Assert.Equal(
            [$"HEAD blossom.primal.net/{sha256}.zip", $"HEAD blossom.data.haus/{sha256}", $"HEAD nostr.download/{sha256}"],
            _degMods.Asked);
    }

    [Fact]
    public async Task AServerThatFailsDoesntHaveTheFile()
    {
        _degMods.Answer("HEAD", new Uri($"https://blossom.primal.net/{RecordedDegMods.NewspapersFile}"), 503);

        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, RecordedDegMods.NewspapersFile[..16]);

        Assert.IsType<HandoffRequired>(result);
        Assert.Equal(4, _degMods.Asked.Count(asked => asked == $"HEAD blossom.primal.net/{RecordedDegMods.NewspapersFile}"));
    }

    [Fact]
    public async Task TakesACatboxFileFromItsLinkWithoutAsking()
    {
        string id = await FileId("starfield", RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration, 0);

        ResolveResult result = await Resolve("starfield", RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration, id);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(result);
        Assert.Equal(("https://files.catbox.moe/wa4qcn.7z", (FileHash?)null), (ticket.Url.AbsoluteUri, ticket.ExpectedHash));
        Assert.Empty(_degMods.Asked);
    }

    [Fact]
    public async Task ChecksAFileFromItsLinkWithTheSha256ItsAuthorGave()
    {
        TestAuthor author = new(4);
        string sha256 = new('c', 64);
        _degMods.Brs.Add(author.Mod("x", "A mod", "Game", 100, [("https://files.catbox.moe/abc.zip", sha256)]));

        ResolveResult result = await Resolve("game", author.PublicKey, "x", sha256[..16]);

        Assert.Equal(new FileHash(FileHashAlgorithm.Sha256, sha256), Assert.IsType<DownloadTicket>(result).ExpectedHash);
    }

    [Fact]
    public async Task TakesAnInternetArchiveDownloadFromItsLink()
    {
        string id = await FileId("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Nipton, 2);

        ResolveResult result = await Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Nipton, id);

        Assert.Equal("https://archive.org/download/NiptonRebuiltRemade/NRR1.3.7z", Assert.IsType<DownloadTicket>(result).Url.AbsoluteUri);
    }

    [Fact]
    public async Task OpensAPageThatLeadsToAFile()
    {
        string id = await FileId("starfield", RecordedDegMods.MusicAuthor, RecordedDegMods.HaloAndDoom, 0);

        ResolveResult result = await Resolve("starfield", RecordedDegMods.MusicAuthor, RecordedDegMods.HaloAndDoom, id);

        Assert.Equal("www.mediafire.com", Assert.IsType<HandoffRequired>(result).PageUrl.Host);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("on a page at www.mediafire.com", StringComparison.Ordinal));
        Assert.Empty(_degMods.Asked);
    }

    [Theory]
    [InlineData("https://github.com/someone/mod/releases/download/v1/mod.zip", true)]
    [InlineData("https://raw.githubusercontent.com/someone/mod/main/mod.zip", true)]
    [InlineData("https://github.com/someone/mod", false)]
    [InlineData("https://archive.org/details/SomeItem", false)]
    [InlineData("https://drive.google.com/file/d/abc/view", false)]
    [InlineData("https://mega.nz/file/abc#key", false)]
    public async Task TakesAFileFromItsLinkOnlyWhereTheLinkGivesTheFile(string url, bool direct)
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("x", "A mod", "Game", 100, [(url, null)]));
        string id = await FileId("game", author.PublicKey, "x", 0);

        ResolveResult result = await Resolve("game", author.PublicKey, "x", id);

        Assert.Equal(direct, result is DownloadTicket);
    }

    [Fact]
    public async Task AFileTheModNoLongerListsOrAModThatsGoneIsNotFound()
    {
        HttpRequestException file = await Assert.ThrowsAsync<HttpRequestException>(
            () => Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, "0123456789abcdef"));
        HttpRequestException mod = await Assert.ThrowsAsync<HttpRequestException>(
            () => Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, "no-such-mod", "0123456789abcdef"));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (file.StatusCode, mod.StatusCode));
    }

    [Fact]
    public async Task DoesntDownloadAModItsModerationHides()
    {
        HttpRequestException hidden = await Assert.ThrowsAsync<HttpRequestException>(
            () => Resolve("pirates, vikings, & knights ii", RecordedDegMods.TestPostAuthor, RecordedDegMods.BlockedPost, "0123456789abcdef"));

        Assert.Contains("moderation hides", hidden.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("57E4FB2C82C6F67C")]
    [InlineData("57e4fb2c")]
    [InlineData("wa4qcn")]
    public async Task RefusesAFileThatIsntDegMods(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Resolve("fallout: new vegas", RecordedDegMods.FalloutAuthor, RecordedDegMods.PoliceGear, id));
    }

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    private DegModsSource Source() => _services.GetServices<IModSource>().OfType<DegModsSource>().Single();

    private Task<ResolveResult> Resolve(string game, string author, string d, string file)
        => Source().ResolveAsync(new ModFileRef(new ModRef(new GameRef(DegMods, game), $"{author}:{d}"), file), handoff: null, TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<ModFile>> Files(string d)
        => await Source().GetFilesAsync(new ModRef(new GameRef(DegMods, "fallout: new vegas"), $"{RecordedDegMods.FalloutAuthor}:{d}"), TestContext.Current.CancellationToken);

    private async Task<string> FileId(string game, string author, string d, int index)
        => (await Source().GetFilesAsync(new ModRef(new GameRef(DegMods, game), $"{author}:{d}"), TestContext.Current.CancellationToken))[index].Reference.Id;
}
