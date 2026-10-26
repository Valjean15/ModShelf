using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Testing;

/// <summary>
/// What every source must do, whatever its site. Each source's test project derives one class from this, so
/// a new source gets these checks without writing them; checks for a capability the source doesn't
/// implement are skipped.
/// </summary>
/// <typeparam name="TSource">The source under test.</typeparam>
public abstract class SourceContractTests<TSource>
    where TSource : IModSource
{
    /// <summary>
    /// The ID the source documents. It's stored in the library database, so it must never change.
    /// </summary>
    protected abstract SourceId ExpectedId { get; }

    /// <summary>
    /// Links the source recognizes, when it implements <see cref="ILinkRecognizer"/>.
    /// </summary>
    protected abstract IReadOnlyList<Uri> OwnLinks { get; }

    [Fact]
    public void KeepsItsDocumentedId()
    {
        Assert.Equal(ExpectedId, CreateSource().Id);
    }

    [Fact]
    public void HasADisplayName()
    {
        Assert.False(string.IsNullOrWhiteSpace(CreateSource().DisplayName));
    }

    [Fact]
    public async Task RefusesToResolveAnotherSourcesFile()
    {
        IDownloadResolver resolver = Capability<IDownloadResolver>();
        ModFileRef foreign = new(new ModRef(new GameRef(new SourceId("someone-else"), "game"), "mod"), "file");

        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveAsync(foreign, handoff: null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    public async Task NeverRecognizesLinksThatMustNotBeFetched(string link)
    {
        ILinkRecognizer recognizer = Capability<ILinkRecognizer>();

        Assert.Null(await recognizer.RecognizeAsync(new Uri(link), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecognizesItsOwnLinksAsItsOwnFiles()
    {
        ILinkRecognizer recognizer = Capability<ILinkRecognizer>();
        Assert.NotEmpty(OwnLinks);

        foreach (Uri link in OwnLinks)
        {
            RecognizedLink? match = await recognizer.RecognizeAsync(link, TestContext.Current.CancellationToken);

            Assert.NotNull(match);
            Assert.Equal(ExpectedId, match.File.Mod.Game.Source);
        }
    }

    [Fact]
    public async Task ResolvesTheFilesItRecognizes()
    {
        ILinkRecognizer recognizer = Capability<ILinkRecognizer>();
        IDownloadResolver resolver = Capability<IDownloadResolver>();

        foreach (Uri link in OwnLinks)
        {
            RecognizedLink match = (await recognizer.RecognizeAsync(link, TestContext.Current.CancellationToken))!;

            Uri? handoff = match.IsHandoff ? link : null;
            Assert.NotNull(await resolver.ResolveAsync(match.File, handoff, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void OffersEachWayToLinkAnAccountOnce()
    {
        IAccountLinker linker = Capability<IAccountLinker>();

        Assert.NotEmpty(linker.Strategies);
        Assert.Equal(linker.Strategies.Count, linker.Strategies.Select(strategy => strategy.Kind).Distinct().Count());
    }

    [Fact]
    public async Task NeverSendsCredentialsAWayItDoesntOffer()
    {
        IAccountLinker linker = Capability<IAccountLinker>();
        AuthKind[] unoffered = [.. Enum.GetValues<AuthKind>().Where(kind => linker.Strategies.All(strategy => strategy.Kind != kind))];
        Assert.SkipWhen(unoffered.Length == 0, $"{typeof(TSource).Name} offers every way to link an account.");

        foreach (AuthKind kind in unoffered)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => linker.GetProfileAsync(new AccountCredentials("some-token"), kind, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task RefusesToDescribeAnotherSourcesGameOrMod()
    {
        IModCatalog catalog = Capability<IModCatalog>();
        GameRef game = new(new SourceId("someone-else"), "game");
        ModRef mod = new(game, "mod");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetGameAsync(game, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetModAsync(mod, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetFilesAsync(mod, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetChangelogsAsync(mod, cancellationToken));
    }

    [Fact]
    public async Task DescribesTheModsItsOwnLinksPointTo()
    {
        ILinkRecognizer recognizer = Capability<ILinkRecognizer>();
        IModCatalog catalog = Capability<IModCatalog>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        foreach (Uri link in OwnLinks)
        {
            RecognizedLink match = (await recognizer.RecognizeAsync(link, cancellationToken))!;
            ModRef mod = match.File.Mod;

            Assert.Equal(mod.Game, (await catalog.GetGameAsync(mod.Game, cancellationToken)).Game);
            Assert.Equal(mod, (await catalog.GetModAsync(mod, cancellationToken)).Mod);
            IReadOnlyList<ModFile> files = await catalog.GetFilesAsync(mod, cancellationToken);
            Assert.Contains(files, file => file.Reference == match.File);
            Assert.All(files, file => Assert.Equal(mod, file.Reference.Mod));
            Assert.NotNull(await catalog.GetChangelogsAsync(mod, cancellationToken));
        }
    }

    [Fact]
    public async Task AFilesPageIsALinkToThatFile()
    {
        ILinkRecognizer recognizer = Capability<ILinkRecognizer>();
        IModCatalog catalog = Capability<IModCatalog>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecognizedLink linked = (await recognizer.RecognizeAsync(OwnLinks[0], cancellationToken))!;
        ModFile file = Assert.Single(await catalog.GetFilesAsync(linked.File.Mod, cancellationToken), listed => listed.Reference == linked.File);
        Assert.SkipWhen(file.PageUrl is null, $"{typeof(TSource).Name} gives its files no page.");

        RecognizedLink? fromPage = await recognizer.RecognizeAsync(file.PageUrl, cancellationToken);

        Assert.Equal(file.Reference, fromPage?.File);
    }

    [Fact]
    public async Task RefusesToSearchAnotherSourcesGame()
    {
        IModSearch search = Capability<IModSearch>();

        await Assert.ThrowsAsync<ArgumentException>(() => search.SearchAsync(
            new GameRef(new SourceId("someone-else"), "game"), new SearchQuery("anything"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A new instance of the source, set up as the test needs, for example against recorded HTTP fixtures.
    /// </summary>
    protected abstract TSource CreateSource();

    private TCapability Capability<TCapability>()
        where TCapability : class, IModSource
    {
        TSource source = CreateSource();
        Assert.SkipUnless(source is TCapability, $"{typeof(TSource).Name} doesn't implement {typeof(TCapability).Name}.");
        return (source as TCapability)!;
    }
}
