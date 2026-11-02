using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// What the screens show mods' pictures with, over fakes: settings in memory, scripted details and saved pictures.
/// </summary>
internal sealed class TestPictures
{
    /// <param name="settings">The settings, when they're another screen's too; new ones otherwise.</param>
    public TestPictures(InMemorySettingsService? settings = null)
    {
        Settings = settings ?? new InMemorySettingsService();
        Pictures = new ModPictures(Settings, Details, Cache, Logger);
    }

    public InMemorySettingsService Settings { get; }

    public ScriptedModDetails Details { get; } = new();

    public ScriptedPictureCache Cache { get; } = new();

    public RecordingLogger<ModPictures> Logger { get; } = new();

    public ModPictures Pictures { get; }
}
