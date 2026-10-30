using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Testing;

namespace ModShelf.Sources.DirectUrl.Tests;

public sealed class DirectUrlSourceTests : SourceContractTests<DirectUrlSource>
{
    private const string Sha256OfAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    protected override SourceId ExpectedId { get; } = new("direct");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new("https://example.com/files/SkyUI_5_2_SE.7z"),
        new("http://example.com/download?id=35407"),
        new($"https://example.com/files/SkyUI_5_2_SE.7z#sha256={Sha256OfAbc}"),
    ];

    [Theory]
    [InlineData("https://example.com/files/SkyUI_5_2_SE.7z")]
    [InlineData("http://example.com/files/SkyUI_5_2_SE.7z")]
    public async Task RecognizesAnyWebLinkAsACatchAll(string link)
    {
        RecognizedLink match = await RecognizeAsync(link);

        Assert.True(match.IsCatchAll);
    }

    [Fact]
    public async Task TheLinkIsTheFilesId()
    {
        Uri link = new("https://Example.com/files/SkyUI_5_2_SE.7z?mirror=2");

        RecognizedLink match = await RecognizeAsync(link.OriginalString);

        Assert.Equal(link.AbsoluteUri, match.File.Id);
        Assert.Equal("example.com", match.File.Mod.Id);
        Assert.Equal("web", match.File.Mod.Game.Key);
    }

    [Theory]
    [InlineData("https://example.com/files/SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z")]
    [InlineData("https://example.com/files/SkyUI%205.2%20SE.7z?mirror=2", "SkyUI 5.2 SE.7z")]
    [InlineData("https://example.com/files/what%3F%20why%3A.zip", "what_ why_.zip")]
    [InlineData("https://example.com/files/..%2F..%2Fevil.exe", ".._.._evil.exe")]
    [InlineData("https://example.com/files/notes.txt.", "notes.txt")]
    [InlineData("https://example.com/files/SkyUI.7z#sha256=" + Sha256OfAbc, "SkyUI.7z")]
    [InlineData("https://example.com/files/aux.zip", "_aux.zip")]
    public async Task SuggestsASafeNameFromTheLastPathSegment(string link, string expected)
    {
        Assert.Equal(expected, (await RecognizeAsync(link)).SuggestedFileName);
    }

    [Fact]
    public async Task ShortensALongNameKeepingItsExtension()
    {
        string name = (await RecognizeAsync($"https://example.com/files/{new string('m', 300)}.zip")).SuggestedFileName!;

        Assert.Equal(LibraryNames.MaxFileBytes, name.Length);
        Assert.EndsWith("m.zip", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/files/")]
    [InlineData("https://example.com/files/%2E%2E")]
    public async Task SuggestsNoNameForALinkThatNamesNoFile(string link)
    {
        Assert.Null((await RecognizeAsync(link)).SuggestedFileName);
    }

    [Fact]
    public async Task ResolvesToTheLinkWithoutItsFragment()
    {
        DownloadTicket ticket = await ResolveAsync($"https://example.com/files/SkyUI.7z?mirror=2#sha256={Sha256OfAbc}");

        Assert.Equal(new Uri("https://example.com/files/SkyUI.7z?mirror=2"), ticket.Url);
        Assert.Empty(ticket.Url.Fragment);
        Assert.Empty(ticket.Headers);
    }

    [Theory]
    [InlineData("md5", FileHashAlgorithm.Md5, "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("SHA1", FileHashAlgorithm.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("sha256", FileHashAlgorithm.Sha256, Sha256OfAbc)]
    [InlineData("sha512", FileHashAlgorithm.Sha512, "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public async Task ReadsTheExpectedHashFromTheFragment(string name, FileHashAlgorithm algorithm, string digest)
    {
        DownloadTicket ticket = await ResolveAsync($"https://example.com/abc.txt#{name}={digest.ToUpperInvariant()}");

        Assert.Equal(new FileHash(algorithm, digest), ticket.ExpectedHash);
    }

    [Theory]
    [InlineData("https://example.com/abc.txt")]
    [InlineData("https://example.com/abc.txt#readme")]
    [InlineData("https://example.com/abc.txt#egg=abc&page=2")]
    public async Task HasNoExpectedHashWhenTheFragmentGivesNone(string link)
    {
        Assert.Null((await ResolveAsync(link)).ExpectedHash);
    }

    [Fact]
    public async Task ReadsAHashBesideOtherFragmentParts()
    {
        DownloadTicket ticket = await ResolveAsync($"https://example.com/abc.txt#egg=abc&sha256={Sha256OfAbc}");

        Assert.Equal(new FileHash(FileHashAlgorithm.Sha256, Sha256OfAbc), ticket.ExpectedHash);
    }

    [Theory]
    [InlineData("https://example.com/abc.txt#sha256=not-hex")]
    [InlineData("https://example.com/abc.txt#sha256=abc123")]
    [InlineData("https://example.com/abc.txt#md5=900150983cd24fb0d6963f7d28e17f72&sha256=" + Sha256OfAbc)]
    [InlineData("https://user:password@example.com/abc.txt")]
    [InlineData("https://token@example.com/abc.txt")]
    public async Task RefusesALinkItCantSaveOrCheck(string link)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RecognizeAsync(link));
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/abc.txt")]
    public async Task RefusesToResolveAFileWhoseIdIsNotAWebLink(string fileId)
    {
        ModFileRef file = new(new ModRef(new GameRef(ExpectedId, "web"), "example.com"), fileId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateSource().ResolveAsync(file, handoff: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesAnotherSourcesFileEvenWhenItsIdIsAWebLink()
    {
        ModFileRef file = new(new ModRef(new GameRef(new SourceId("nexus"), "web"), "example.com"), "https://example.com/abc.txt");

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateSource().ResolveAsync(file, handoff: null, TestContext.Current.CancellationToken));
    }

    protected override DirectUrlSource CreateSource() => new();

    private async Task<RecognizedLink> RecognizeAsync(string link)
    {
        RecognizedLink? match = await CreateSource().RecognizeAsync(new Uri(link), TestContext.Current.CancellationToken);
        Assert.NotNull(match);
        return match;
    }

    private async Task<DownloadTicket> ResolveAsync(string link)
    {
        RecognizedLink match = await RecognizeAsync(link);
        ResolveResult result = await CreateSource().ResolveAsync(match.File, handoff: null, TestContext.Current.CancellationToken);
        return Assert.IsType<DownloadTicket>(result);
    }
}
