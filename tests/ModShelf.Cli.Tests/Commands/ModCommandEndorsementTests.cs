using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod endorse</c> and <c>mod abstain</c> against a fake site that keeps the account's endorsements.
/// </summary>
public sealed class ModCommandEndorsementTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeCatalogSite _catalog = new();
    private readonly InProcessCli _cli;

    public ModCommandEndorsementTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [_catalog]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task EndorsesTheModOfALink()
    {
        CliResult endorsed = await _cli.RunAsync(["mod", "endorse", FileLink("42", "12").AbsoluteUri], Cancellation);

        Assert.Equal(Done, endorsed.ExitCode);
        Assert.Equal("Endorsed on Fake catalog site: mod 42 of game.", endorsed.Output.Trim());
        Assert.Equal(Endorsement.Endorsed, _catalog.Endorsed[Mod("42")]);
    }

    [Fact]
    public async Task AbstainsFromTheModOfALinkEvenOneEndorsedBefore()
    {
        await _cli.RunAsync(["mod", "endorse", ModLink("42").AbsoluteUri], Cancellation);

        CliResult abstained = await _cli.RunAsync(["mod", "abstain", ModLink("42").AbsoluteUri], Cancellation);

        Assert.Equal(Done, abstained.ExitCode);
        Assert.Equal("Abstained on Fake catalog site: mod 42 of game.", abstained.Output.Trim());
        Assert.Equal(Endorsement.Abstained, _catalog.Endorsed[Mod("42")]);
    }

    [Fact]
    public async Task SaysItInTheSitesOwnWords()
    {
        _catalog.Words = new() { Approve = "like", Unapprove = "pass" };

        CliResult liked = await _cli.RunAsync(["mod", "endorse", ModLink("42").AbsoluteUri], Cancellation);
        CliResult passed = await _cli.RunAsync(["mod", "abstain", ModLink("43").AbsoluteUri], Cancellation);

        Assert.Equal("Liked on Fake catalog site: mod 42 of game.", liked.Output.Trim());
        Assert.Equal("Passed on Fake catalog site: mod 43 of game.", passed.Output.Trim());
    }

    [Theory]
    [InlineData("endorse", "not a link", BadInput, "isn't a complete link")]
    [InlineData("endorse", "ftp://elsewhere.example/mod.zip", BadInput, "No source knows this link.")]
    [InlineData("abstain", "https://elsewhere.example/files/mod.zip", BadInput, "isn't to a mod on a site where ModShelf can endorse mods.")]
    [InlineData("endorse", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("endorse", "https://catalog.example/game/mods/401", Failed, "The site needs a linked account to endorse the mod, or refused the one linked. Link it with account link")]
    [InlineData("endorse", "https://catalog.example/game/mods/2", Failed, "The site wouldn't endorse the mod: You need to download the mod before endorsing it.")]
    [InlineData("abstain", "https://catalog.example/game/mods/503", Failed, "The site wouldn't abstain from the mod: Service unavailable.")]
    public async Task SaysWhyItCantEndorseOrAbstain(string command, string link, int exitCode, string message)
    {
        CliResult refused = await _cli.RunAsync(["mod", command, link], Cancellation);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.Empty(refused.Output);
        Assert.Empty(_catalog.Endorsed);
    }

    private static ModRef Mod(string id) => new(new GameRef(FakeCatalogSite.SourceId, "game"), id);
}
