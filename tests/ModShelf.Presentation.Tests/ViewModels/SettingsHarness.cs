using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The Settings screen over fakes: settings in memory, a library, a mover, and dialogs the test answers.
/// </summary>
internal sealed class SettingsHarness
{
    public SettingsHarness(bool protectedByOs = true)
    {
        Mover = new ScriptedLibraryMover(Settings);
        Screen = new SettingsViewModel(Settings, Library, Mover, new FixedSecretStore(protectedByOs), Themes, Dialogs, Logger);
    }

    public InMemorySettingsService Settings { get; } = new();

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedLibraryMover Mover { get; }

    public RecordingThemeSwitcher Themes { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public RecordingLogger<SettingsViewModel> Logger { get; } = new();

    public SettingsViewModel Screen { get; }

    public Task StartAsync() => Screen.StartAsync(TestContext.Current.CancellationToken);
}
