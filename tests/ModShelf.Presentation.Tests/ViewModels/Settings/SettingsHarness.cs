using ModShelf.Application.Diagnostics;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The Settings screen over fakes: settings in memory, a library, a mover, dialogs the test answers, the sites' links
/// as the test sets them, and the About part with ModShelf's own updates.
/// </summary>
internal sealed class SettingsHarness
{
    /// <param name="releases">ModShelf's releases, as the test sets them up; by default, an installed ModShelf.</param>
    /// <param name="example">The example site, beside Nexus Mods.</param>
    public SettingsHarness(bool protectedByOs = true, ScriptedAppReleases? releases = null, ExampleSite? example = null)
    {
        Releases = releases ?? new ScriptedAppReleases();
        Mover = new ScriptedLibraryMover(Settings);
        About = new AboutViewModel(new AppInfo("0.9.0", "a60d76b"), Diagnostics, Diagnostics, new RecordingClipboard(), new ScriptedFolders(), new RecordingLogger<AboutViewModel>());
        Links = new LinksViewModel(LinkHandlers, Navigation, UiThread, LinksLogger);
        AppUpdate = new AppUpdateViewModel(Releases, Exit, UiThread, AppUpdateLogger);
        Pictures = new TestPictures(Settings);
        PicturesCard = new PicturesViewModel(Settings, Pictures.Pictures, PicturesLogger);
        GameSettings = new GameSettingsOpener(Navigation);
        Screens = new ScreenOpener(Navigation);
        Screen = new SettingsViewModel(
            Settings,
            Library,
            Mover,
            Rescan,
            new FixedSecretStore(protectedByOs),
            Themes,
            Tray,
            Dialogs,
            example is null ? new FakeSourceRegistry(Catalog) : new FakeSourceRegistry(Catalog, example),
            Lookup,
            Hidden,
            GameSettings,
            Screens,
            Sites,
            UiThread,
            PicturesCard,
            Pictures.Pictures,
            Links,
            About,
            AppUpdate,
            Logger);
    }

    public InMemorySettingsService Settings { get; } = new();

    public TestPictures Pictures { get; }

    public RecordingLogger<PicturesViewModel> PicturesLogger { get; } = new();

    public PicturesViewModel PicturesCard { get; }

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedCatalog Catalog { get; } = new(TestData.Nexus, "Nexus Mods");

    public HiddenGames Hidden { get; } = new();

    public ScriptedSitesInUse Sites { get; } = new();

    /// <summary>
    /// The sites' games, for Add a game….
    /// </summary>
    public ScriptedLookup Lookup { get; } = new();

    public GameSettingsOpener GameSettings { get; }

    /// <summary>
    /// What opens the settings at a category, as quick search does.
    /// </summary>
    public ScreenOpener Screens { get; }

    public ScriptedLibraryMover Mover { get; }

    public ScriptedRescan Rescan { get; } = new();

    public RecordingThemeSwitcher Themes { get; } = new();

    public ScriptedTrayHost Tray { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public FixedDiagnostics Diagnostics { get; } = new();

    public ScriptedLinkHandlers LinkHandlers { get; } = new();

    public NavigationService Navigation { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public RecordingLogger<LinksViewModel> LinksLogger { get; } = new();

    public LinksViewModel Links { get; }

    public AboutViewModel About { get; }

    public ScriptedAppReleases Releases { get; }

    public RecordingAppExit Exit { get; } = new();

    public RecordingLogger<AppUpdateViewModel> AppUpdateLogger { get; } = new();

    public AppUpdateViewModel AppUpdate { get; }

    public RecordingLogger<SettingsViewModel> Logger { get; } = new();

    public SettingsViewModel Screen { get; }

    public Task StartAsync() => Screen.StartAsync(TestContext.Current.CancellationToken);
}
