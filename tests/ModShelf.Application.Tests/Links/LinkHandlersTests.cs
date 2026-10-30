using ModShelf.Application.Links;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Links;

/// <summary>
/// Which app opens each site's handoff links, and ModShelf taking them and giving them back.
/// </summary>
public sealed class LinkHandlersTests
{
    private readonly FakeModSite _site = new();
    private readonly MemorySchemeRegistration _registration = new();
    private readonly RecordingLogger<LinkHandlers> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsEachSiteThatHandsOverWithLinksOfItsOwn()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex");

        IReadOnlyList<LinkHandler> handlers = await Create(new CatalogOnlySource(new SourceId("plain"))).ListAsync(Cancellation);

        Assert.Equal(
            new LinkHandler(FakeModSite.SourceId, "Fake mod site", "fakemods", new SchemeState(SchemeHandlerKind.OtherApp, "Vortex")),
            Assert.Single(handlers));
    }

    [Fact]
    public async Task TakingTheLinksRegistersTheSchemeAndSaysWhoHadThem()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex");

        LinkHandler taken = await Create().TakeAsync(FakeModSite.SourceId, Cancellation);

        Assert.Equal(["Register fakemods"], _registration.Calls);
        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), taken.State);
        Assert.StartsWith("Information: ModShelf opens fake-mods's fakemods:// links now", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GivingTheLinksBackUnregistersTheScheme()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex");

        LinkHandler given = await Create().GiveBackAsync(FakeModSite.SourceId, Cancellation);

        Assert.Equal(["Unregister fakemods"], _registration.Calls);
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), given.State);
        Assert.StartsWith("Information: ModShelf gave fake-mods's fakemods:// links back", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASiteWithoutLinksOfItsOwnHasNoneToTake()
    {
        LinkHandlers handlers = Create(new CatalogOnlySource(new SourceId("plain")));

        await Assert.ThrowsAsync<ArgumentException>(() => handlers.TakeAsync(new SourceId("plain"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => handlers.GiveBackAsync(new SourceId("unknown"), Cancellation));
        Assert.Empty(_registration.Calls);
    }

    [Fact]
    public async Task ARegisterThatCantBeChangedIsNeitherLoggedNorHidden()
    {
        _registration.Failure = new UnauthorizedAccessException("The registry refused.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create().TakeAsync(FakeModSite.SourceId, Cancellation));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AnUnavailableRegisterRefusesToTakeThem()
    {
        _registration.States["fakemods"] = SchemeState.Unavailable;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().TakeAsync(FakeModSite.SourceId, Cancellation));
    }

    private LinkHandlers Create(params ModShelf.Sources.Abstractions.IModSource[] others)
        => new(new SourceRegistry([_site, .. others]), _registration, _logger);
}
