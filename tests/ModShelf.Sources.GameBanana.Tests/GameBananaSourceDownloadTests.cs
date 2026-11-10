using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// GameBanana's link for a file, read afresh each time, with its checksum.
/// </summary>
public sealed class GameBananaSourceDownloadTests : IDisposable
{
    private readonly GameBananaHarness _harness = new();

    public GameBananaSourceDownloadTests()
    {
        _harness.Api.Serve("mod");
    }

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(RecordedGameBananaApi.MainFile, "29eb8bbfbb91ca848fb84d61d355d493")]
    [InlineData(RecordedGameBananaApi.ArchivedFile, "ffdde8907168086d88fa43efef107f4b")]
    public async Task AFilesTicketIsGameBananasLinkWithItsChecksum(string file, string md5)
    {
        ResolveResult result = await ResolveAsync(file);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(result);
        Assert.Equal(new Uri($"https://gamebanana.com/dl/{file}"), ticket.Url);
        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, md5), ticket.ExpectedHash);
        Assert.Empty(ticket.Headers);
    }

    [Fact]
    public async Task AskesForTheLinkEachTime()
    {
        await ResolveAsync(RecordedGameBananaApi.MainFile);
        await ResolveAsync(RecordedGameBananaApi.MainFile);

        Assert.Equal(2, _harness.Asked.Count);
    }

    [Fact]
    public async Task AFileGameBananaNoLongerListsIsNotFound()
    {
        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => ResolveAsync("999"));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ALinkOffGameBananaIsntFollowed()
    {
        _harness.Api.Answer(
            "/apiv11/Mod/1/ProfilePage",
            answer => answer.WithBody("""{"_idRow": 1, "_aFiles": [{"_idRow": 5, "_sFile": "a.7z", "_sDownloadUrl": "https://elsewhere.example/a.7z"}]}"""));

        ResolveResult result = await _harness.Source.ResolveAsync(new ModFileRef(new ModRef(GameBananaHarness.Game, "1"), "5"), null, GameBananaHarness.Cancellation);

        Assert.Equal(new Uri("https://gamebanana.com/dl/5"), Assert.IsType<DownloadTicket>(result).Url);
    }

    [Theory]
    [InlineData("gamebanana", "p5r", "1")]
    [InlineData("gamebanana", "16951", "file")]
    [InlineData("nexus", "16951", "1")]
    public async Task RefusesAFileThatIsntGameBananas(string source, string game, string file)
    {
        ModFileRef foreign = new(new ModRef(new GameRef(new SourceId(source), game), "440088"), file);

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.ResolveAsync(foreign, null, GameBananaHarness.Cancellation));
        Assert.Empty(_harness.Asked);
    }

    private Task<ResolveResult> ResolveAsync(string file)
        => _harness.Source.ResolveAsync(new ModFileRef(GameBananaHarness.Mod, file), handoff: null, GameBananaHarness.Cancellation);
}
