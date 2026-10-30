using ModShelf.Application.Diagnostics;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The Settings screen over fakes: settings in memory, a library, a mover, dialogs the test answers, the sites' links
/// as the test sets them, and the About part.
/// </summary>
internal sealed class SettingsHarness
{
    public SettingsHarness(bool protectedByOs = true)
    {
        Mover = new ScriptedLibraryMover(Settings);
        About = new AboutViewModel(new AppInfo("0.9.0", "a60d76b"), Diagnostics, Diagnostics, new RecordingClipboard(), new ScriptedFolders(), new RecordingLogger<AboutViewModel>());
        Links = new LinksViewModel(LinkHandlers, Navigation, UiThread, LinksLogger);
        Screen = new SettingsViewModel(Settings, Library, Mover, new FixedSecretStore(protectedByOs), Themes, Tray, Dialogs, Links, About, Logger);
    }

    public InMemorySettingsService Settings { get; } = new();

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedLibraryMover Mover { get; }

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

    public RecordingLogger<SettingsViewModel> Logger { get; } = new();

    public SettingsViewModel Screen { get; }

    public Task StartAsync() => Screen.StartAsync(TestContext.Current.CancellationToken);
}
