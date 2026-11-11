using ModShelf.Application.Diagnostics;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.Accounts;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions;
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
    /// <param name="byName">A site, beside Nexus Mods, that finds its games by name.</param>
    public SettingsHarness(bool protectedByOs = true, ScriptedAppReleases? releases = null, ExampleSite? example = null, ScriptedCatalog? byName = null)
    {
        Releases = releases ?? new ScriptedAppReleases();
        Mover = new ScriptedLibraryMover(Settings);
        About = new AboutViewModel(new AppInfo("0.9.0", "a60d76b"), Diagnostics, Diagnostics, new RecordingClipboard(), new ScriptedFolders(), new RecordingLogger<AboutViewModel>());
        Links = new LinksViewModel(LinkHandlers, Navigation, UiThread, LinksLogger);
        Agents = new AgentsViewModel(Settings, AgentServer, ClaudeDesktop, Clipboard, Dialogs, Navigation, UiThread, AgentsLogger);
        AppUpdate = new AppUpdateViewModel(Releases, Exit, UiThread, AppUpdateLogger);
        Pictures = new TestPictures(Settings);
        PicturesCard = new PicturesViewModel(Settings, Pictures.Pictures, PicturesLogger);
        BrowserFolder = new BrowserFolderViewModel(Settings, Folders, Dialogs, BrowserFolderLogger);
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
            new FakeSourceRegistry([Catalog, .. example is null ? [] : new IModSource[] { example }, .. byName is null ? [] : new IModSource[] { byName }]),
            Lookup,
            Hidden,
            GameSettings,
            Screens,
            Navigation,
            Sites,
            UiThread,
            PicturesCard,
            BrowserFolder,
            Pictures.Pictures,
            Links,
            Agents,
            About,
            AppUpdate,
            Accounts.Screen,
            Logger);
    }

    /// <summary>
    /// Settings › Accounts, over the accounts' own fakes.
    /// </summary>
    public AccountsHarness Accounts { get; } = new();

    public InMemorySettingsService Settings { get; } = new();

    public TestPictures Pictures { get; }

    public RecordingLogger<PicturesViewModel> PicturesLogger { get; } = new();

    public FixedUserFolders Folders { get; } = new();

    public BrowserFolderViewModel BrowserFolder { get; }

    public RecordingLogger<BrowserFolderViewModel> BrowserFolderLogger { get; } = new();

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

    public ScriptedAgentServer AgentServer { get; } = new();

    public ScriptedClaudeDesktop ClaudeDesktop { get; } = new();

    public RecordingClipboard Clipboard { get; } = new();

    public RecordingLogger<AgentsViewModel> AgentsLogger { get; } = new();

    public AgentsViewModel Agents { get; }

    public AboutViewModel About { get; }

    public ScriptedAppReleases Releases { get; }

    public RecordingAppExit Exit { get; } = new();

    public RecordingLogger<AppUpdateViewModel> AppUpdateLogger { get; } = new();

    public AppUpdateViewModel AppUpdate { get; }

    public RecordingLogger<SettingsViewModel> Logger { get; } = new();

    public SettingsViewModel Screen { get; }

    public Task StartAsync() => Screen.StartAsync(TestContext.Current.CancellationToken);
}
