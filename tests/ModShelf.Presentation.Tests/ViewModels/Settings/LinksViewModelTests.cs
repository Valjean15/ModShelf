using ModShelf.Application.Links;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Links from websites, on the Settings screen: which app opens each site's links, and taking them or giving them back.
/// </summary>
public sealed class LinksViewModelTests
{
    private static readonly SourceId Nexus = new("nexus");

    private readonly SettingsHarness _harness = new();

    private LinksViewModel Links => _harness.Links;

    [Theory]
    [InlineData(SchemeHandlerKind.OtherApp, "Vortex", "Vortex opens nxm:// links from Nexus Mods.", true, false)]
    [InlineData(SchemeHandlerKind.OtherApp, null, "Another app opens nxm:// links from Nexus Mods.", true, false)]
    [InlineData(SchemeHandlerKind.None, null, "No app opens nxm:// links from Nexus Mods.", true, false)]
    [InlineData(SchemeHandlerKind.ModShelf, null, "ModShelf opens nxm:// links from Nexus Mods.", false, true)]
    [InlineData(SchemeHandlerKind.Unavailable, null, "ModShelf can open nxm:// links from Nexus Mods once it's installed.", false, false)]
    public async Task SaysWhichAppOpensEachSitesLinksOnceSettingsStart(SchemeHandlerKind kind, string? app, string said, bool take, bool giveBack)
    {
        Show(new SchemeState(kind, app));

        await _harness.StartAsync();

        LinkHandlerViewModel site = Assert.Single(Links.Sites);
        Assert.Equal(said, site.StateText);
        Assert.Equal((take, take), (site.OffersTake, site.TakeCommand.CanExecute(null)));
        Assert.Equal((giveBack, giveBack), (site.OffersGiveBack, site.GiveBackCommand.CanExecute(null)));
        Assert.Same(Links, _harness.Screen.Links);
    }

    [Fact]
    public async Task TakingThemFromVortexOffersToGiveThemBack()
    {
        Show(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"));
        await _harness.StartAsync();
        LinkHandlerViewModel site = Links.Sites[0];

        await site.TakeCommand.ExecuteAsync(null);

        Assert.Equal(["List", "Take nexus"], _harness.LinkHandlers.Calls);
        Assert.Equal("ModShelf opens nxm:// links from Nexus Mods.", site.StateText);
        Assert.Equal("Give them back to Vortex", site.GiveBackText);
        Assert.Equal((false, true), (site.OffersTake, site.OffersGiveBack));
        Assert.Equal(string.Empty, site.Note);
    }

    [Fact]
    public async Task AnAppChosenInTheSystemsSettingsIsSaidToStillOpenThem()
    {
        Show(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"));
        _harness.LinkHandlers.AfterTake = state => state;
        await _harness.StartAsync();
        LinkHandlerViewModel site = Links.Sites[0];

        await site.TakeCommand.ExecuteAsync(null);

        Assert.Equal("Vortex still opens them: it was chosen in the system's own settings. Choose ModShelf for nxm:// links there.", site.Note);
        Assert.False(site.IsNoteAProblem);
    }

    [Fact]
    public async Task GivingThemBackSaysWhoOpensThemNow()
    {
        Show(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"));
        await _harness.StartAsync();
        LinkHandlerViewModel site = Links.Sites[0];

        await site.GiveBackCommand.ExecuteAsync(null);

        Assert.Equal(["List", "GiveBack nexus"], _harness.LinkHandlers.Calls);
        Assert.Equal("Vortex opens nxm:// links from Nexus Mods.", site.StateText);
        Assert.Equal(string.Empty, site.Note);
    }

    [Fact]
    public async Task WithNoAppBeforeItOnlyStops()
    {
        Show(new SchemeState(SchemeHandlerKind.ModShelf));
        await _harness.StartAsync();

        Assert.Equal("Stop opening them", Links.Sites[0].GiveBackText);
    }

    [Fact]
    public async Task ASystemWithNoOtherAppKeepsThemWithModShelf()
    {
        Show(new SchemeState(SchemeHandlerKind.ModShelf));
        _harness.LinkHandlers.AfterGiveBack = state => state;
        await _harness.StartAsync();
        LinkHandlerViewModel site = Links.Sites[0];

        await site.GiveBackCommand.ExecuteAsync(null);

        Assert.Equal("No other app opens nxm:// links, so the system still opens them with ModShelf.", site.Note);
    }

    [Fact]
    public async Task ASystemThatCantRegisterSaysSo()
    {
        Show(new SchemeState(SchemeHandlerKind.None));
        await _harness.StartAsync();
        _harness.LinkHandlers.Refusal = new InvalidOperationException("Can't.");
        LinkHandlerViewModel site = Links.Sites[0];

        await site.TakeCommand.ExecuteAsync(null);

        Assert.Equal(("ModShelf can't open links on this system.", true), (site.Note, site.IsNoteAProblem));
        Assert.Empty(_harness.LinksLogger.Entries);
        Assert.True(site.TakeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARegisterThatCantBeChangedSaysWhy()
    {
        Show(new SchemeState(SchemeHandlerKind.ModShelf));
        await _harness.StartAsync();
        _harness.LinkHandlers.Refusal = new UnauthorizedAccessException("Access to the registry key is denied.");
        LinkHandlerViewModel site = Links.Sites[0];

        await site.GiveBackCommand.ExecuteAsync(null);

        Assert.Equal(("The system's apps for links couldn't be changed: Access to the registry key is denied.", true), (site.Note, site.IsNoteAProblem));
        Assert.Equal("ModShelf opens nxm:// links from Nexus Mods.", site.StateText);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLogged()
    {
        Show(new SchemeState(SchemeHandlerKind.None));
        await _harness.StartAsync();
        _harness.LinkHandlers.Refusal = new FormatException("Something broke.");

        await Links.Sites[0].TakeCommand.ExecuteAsync(null);

        Assert.Equal("Something went wrong. The log has the details.", Links.Sites[0].Note);
        Assert.StartsWith("Error: Making ModShelf open a site's links failed System.FormatException", Assert.Single(_harness.LinksLogger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinksThatCantBeReadAreSaid()
    {
        Show(new SchemeState(SchemeHandlerKind.None));
        _harness.LinkHandlers.Refusal = new IOException("The disk is gone.");

        await _harness.StartAsync();

        Assert.Equal("Which app opens links couldn't be read: The disk is gone.", Links.Problem);
        Assert.Empty(Links.Sites);
        Assert.Empty(_harness.LinksLogger.Entries);
    }

    [Fact]
    public async Task ShowingSettingsReadsThemAgainInPlace()
    {
        Show(new SchemeState(SchemeHandlerKind.ModShelf));
        await _harness.StartAsync();
        LinkHandlerViewModel site = Links.Sites[0];

        // Vortex took them back as it started.
        _harness.LinkHandlers.Handlers[0] = _harness.LinkHandlers.Handlers[0] with { State = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex") };
        _harness.Navigation.NavigateTo(Screen.Settings);
        await Task.Yield();

        Assert.Equal(["List", "List"], _harness.LinkHandlers.Calls);
        Assert.Same(site, Assert.Single(Links.Sites));
        Assert.Equal("Vortex opens nxm:// links from Nexus Mods.", site.StateText);
    }

    [Fact]
    public async Task OtherScreensDontReadThemAgain()
    {
        await _harness.StartAsync();

        _harness.Navigation.NavigateTo(Screen.Downloads);

        Assert.Equal(["List"], _harness.LinkHandlers.Calls);
    }

    private void Show(SchemeState state) => _harness.LinkHandlers.Handlers.Add(new LinkHandler(Nexus, "Nexus Mods", "nxm", state));
}
