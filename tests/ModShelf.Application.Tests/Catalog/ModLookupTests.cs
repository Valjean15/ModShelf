using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Catalog;

public sealed class ModLookupTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    [Fact]
    public async Task AsksTheLinksSourceAboutItsMod()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await LookUpAsync(site);

        Assert.Equal(site.Game, page.Game);
        Assert.Equal(site.Mod, page.Mod);
        Assert.Equal(site.Files.Select(file => file.Reference), page.Files.Select(file => file.Reference));
        Assert.Equal(site.Changelogs, page.Changelogs);
        Assert.Equal(site.LinkedFile, page.LinkedFile);
        Assert.Equal(
            ["GetChangelogsAsync", "GetFilesAsync", "GetGameAsync", "GetModAsync", "RecognizeAsync"],
            site.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASiteSpecificMatchWinsOverACatchAll()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        ScriptedLinkRecognizer catchAll = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        ModPage page = await LookUpAsync(catchAll, site);

        Assert.Equal(site.LinkedFile, page.LinkedFile);
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => LookUpAsync(ScriptedLinkRecognizer.Never(TestData.Nexus)));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task SaysWhenTheLinksSourceHasNoCatalog()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => LookUpAsync(direct));

        Assert.Contains("isn't to a mod", refused.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "GetGameAsync", new AuthenticationFailedException("The site refused the account.") },
        { "GetModAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
        { "GetFilesAsync", new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable) },
        { "GetChangelogsAsync", new HttpRequestException("No route to host.") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSourceRefuses(string method, Exception failure)
    {
        FakeCatalogSource site = new(TestData.Nexus);
        site.Failures[method] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => LookUpAsync(site));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        ModLookup lookup = new(new SourceRegistry([new FakeCatalogSource(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookUpAsync(
            new Uri("skyrimspecialedition/mods/12604", UriKind.Relative), TestContext.Current.CancellationToken));
    }

    private static Task<ModPage> LookUpAsync(params IModSource[] sources)
        => new ModLookup(new SourceRegistry(sources)).LookUpAsync(Link, TestContext.Current.CancellationToken);
}
