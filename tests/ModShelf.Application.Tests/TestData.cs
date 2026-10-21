using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests;

/// <summary>
/// Shared sample values.
/// </summary>
internal static class TestData
{
    public static SourceId Nexus { get; } = new("nexus");

    public static SourceId ModIo { get; } = new("mod-io");

    public static string TargetDirectory { get; } = Path.Combine(Path.GetTempPath(), "ModShelf", "Downloads");

    public static DateTimeOffset Now { get; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A file on <paramref name="source"/>; each <paramref name="fileId"/> is a different file.
    /// </summary>
    public static ModFileRef File(SourceId source, string fileId)
        => new(new ModRef(new GameRef(source, "skyrimspecialedition"), "12604"), fileId);
}
