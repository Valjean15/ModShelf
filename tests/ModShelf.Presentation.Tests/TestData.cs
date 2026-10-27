using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests;

/// <summary>
/// Sample values the tests share.
/// </summary>
internal static class TestData
{
    public const string FileName = "SkyUI_5_2_SE-12604-5-2SE.7z";

    public static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public static readonly SourceId Nexus = new("nexus");

    public static readonly Uri ModPage = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    public static readonly Uri HandoffPage = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");

    public static readonly Uri HandoffLink = new("nxm://skyrimspecialedition/mods/12604/files/35407?key=a-key&expires=1761566400&user_id=1");

    /// <summary>
    /// The library's root, an absolute path on every system.
    /// </summary>
    public static string LibraryRoot { get; } = Path.Combine(Path.GetTempPath(), "ModShelf Library");

    /// <summary>
    /// A folder outside the library.
    /// </summary>
    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "My Downloads");

    public static ModFileRef File(string id = "35407") => new(new ModRef(new GameRef(Nexus, "skyrimspecialedition"), "12604"), id);

    /// <summary>
    /// A job as the queue lists it: in a folder outside the library unless told otherwise, failed for the network, and
    /// waiting on <see cref="HandoffPage"/> for a handoff.
    /// </summary>
    public static DownloadJobSnapshot Job(
        string fileName = FileName,
        DownloadJobStatus status = DownloadJobStatus.Queued,
        string? folder = null,
        DateTimeOffset? createdAt = null)
        => new(
            DownloadJobId.New(),
            File(),
            folder ?? Folder,
            fileName,
            status,
            BytesDownloaded: 0,
            TotalBytes: null,
            FailureReason: status == DownloadJobStatus.Failed ? DownloadFailureReason.Network : null,
            createdAt ?? Now,
            status == DownloadJobStatus.AwaitingHandoff ? HandoffPage : null);
}
