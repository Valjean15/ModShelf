using ModShelf.Application.Links;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

/// <summary>
/// ModShelf registering its own <c>modshelf://</c> links before a notice, so that a click on it opens ModShelf.
/// </summary>
public sealed class NoticeClicksTests
{
    private readonly MemorySchemeRegistration _links = new();
    private readonly RecordingLogger<NoticeClicksTests> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegistersModShelfForItsLinksBeforeAClickOpensOne()
    {
        NoticeClicks clicks = new(_links, _logger);

        Assert.Equal("modshelf://updates", await clicks.LinkAsync(Cancellation));
        Assert.Equal("modshelf://updates", await clicks.LinkAsync(Cancellation));

        Assert.Equal(["Register modshelf"], _links.Calls);
        Assert.Equal(SchemeHandlerKind.ModShelf, _links.States["modshelf"].Kind);
    }

    [Fact]
    public async Task TakesTheLinksBackFromAnotherModShelf()
    {
        _links.States["modshelf"] = new SchemeState(SchemeHandlerKind.OtherApp, "Another ModShelf");

        Assert.Equal("modshelf://updates", await new NoticeClicks(_links, _logger).LinkAsync(Cancellation));

        Assert.Equal(["Register modshelf"], _links.Calls);
    }

    [Fact]
    public async Task WithoutTheDesktopAppAClickOpensNothing()
    {
        _links.States["modshelf"] = SchemeState.Unavailable;

        Assert.Null(await new NoticeClicks(_links, _logger).LinkAsync(Cancellation));

        Assert.Empty(_links.Calls);
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task ALinkThatCantBeRegisteredOpensNothingAndIsLogged(Type failure)
    {
        _links.Failure = (Exception)Activator.CreateInstance(failure, "Refused.")!;

        Assert.Null(await new NoticeClicks(_links, _logger).LinkAsync(Cancellation));

        string entry = Assert.Single(_logger.Entries);
        Assert.StartsWith("Warning: A click on a notice can't open ModShelf", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingUnregistersTheLinks()
    {
        NoticeClicks clicks = new(_links, _logger);
        await clicks.LinkAsync(Cancellation);

        await clicks.RemoveAsync(Cancellation);

        Assert.Equal(["Register modshelf", "Unregister modshelf"], _links.Calls);
        Assert.Equal(SchemeState.None, _links.States["modshelf"]);
    }
}
