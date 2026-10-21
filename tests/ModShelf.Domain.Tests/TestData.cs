using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests;

/// <summary>
/// Shared sample values: a Nexus Mods file, a download folder and a fixed point in time.
/// </summary>
internal static class TestData
{
    public const string Md5 = "0123456789abcdef0123456789abcdef";

    public static SourceId Nexus { get; } = new("nexus");

    public static GameRef Skyrim { get; } = new(Nexus, "skyrimspecialedition");

    public static ModRef SkyUi { get; } = new(Skyrim, "12604");

    public static ModFileRef SkyUiMainFile { get; } = new(SkyUi, "35407");

    public static string TargetDirectory { get; } = Path.Combine(Path.GetTempPath(), "ModShelf", "Downloads");

    public static DateTimeOffset CreatedAt { get; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
}
