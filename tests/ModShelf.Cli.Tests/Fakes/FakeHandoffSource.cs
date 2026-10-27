using System.Collections.Concurrent;
using System.Net;
using System.Web;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A site that, as Nexus Mods does for free accounts, lets a file be downloaded only once the user has started the
/// download on its website: until it gets a handoff link with the right key, it asks for one. Its files are at
/// <c>https://handoff.example/files/{id}</c>, its handoff links are <c>fakehandoff://files/{id}?key=…</c>, and every
/// file downloads from <paramref name="fileUrl"/>. Links under <c>/locked/</c> need an account it doesn't have, and
/// links under <c>/missing/</c> are to files it doesn't have. Every file is of one mod, <see cref="ModName"/>, which its
/// catalog describes, so its files can go into the library.
/// </summary>
internal sealed class FakeHandoffSource(Uri fileUrl) : ILinkRecognizer, IDownloadResolver, IModCatalog
{
    public const string ModName = "Handoff Mod";

    public const string GoodKey = "handoff-key-3f9a1c";

    private const string Host = "handoff.example";
    private const string Scheme = "fakehandoff";

    private static readonly string[] FileIds = ["1", "2", "3"];

    private readonly ConcurrentQueue<Uri?> _handoffs = new();

    public static SourceId SourceId { get; } = new("fake-handoff");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake handoff site";

    /// <summary>
    /// The handoff link of every resolve so far, in order.
    /// </summary>
    public IReadOnlyList<Uri?> Handoffs => [.. _handoffs];

    public static Uri FileLink(string fileId) => new($"https://{Host}/files/{fileId}");

    public static Uri HandoffLink(string fileId, string key = GoodKey) => new($"{Scheme}://files/{fileId}?key={key}");

    /// <summary>
    /// The page where the user starts the download of <paramref name="fileId"/>.
    /// </summary>
    public static Uri Page(string fileId) => new($"https://{Host}/files/{fileId}/download");

    public static string FileNameOf(string fileId) => $"handoff-{fileId}.zip";

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        bool isHandoff = link.Scheme == Scheme;
        if (!isHandoff && link.Host != Host)
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        string[] path = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fileId = isHandoff ? path[^1] : path[1];
        return (isHandoff ? "files" : path[0]) switch
        {
            "locked" => throw new AuthenticationFailedException("The site wants an account first."),
            "missing" => throw new HttpRequestException("The site has no such file.", null, HttpStatusCode.NotFound),
            _ => Task.FromResult<RecognizedLink?>(new RecognizedLink(File(fileId), FileNameOf(fileId), isHandoff: isHandoff)),
        };
    }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        _handoffs.Enqueue(handoff);
        string? key = handoff is null ? null : HttpUtility.ParseQueryString(handoff.Query)["key"];
        return Task.FromResult<ResolveResult>(key == GoodKey ? new DownloadTicket(fileUrl) : new HandoffRequired(Page(file.Id)));
    }

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => Task.FromResult(new GameInfo(game, "Handoff Game"));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult(new ModInfo(modRef, ModName, author: "Someone", pageUrl: new Uri($"https://{Host}/mod")));

    /// <summary>
    /// Files 1 to 3, each the update of the one before, at versions 1.1 to 1.3, with their pages.
    /// </summary>
    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModFile>>(
        [
            .. FileIds.Select((id, index) => new ModFile(
                File(id),
                $"File {id}",
                FileNameOf(id),
                $"1.{id}",
                ModFileCategory.Main,
                null,
                DateTimeOffset.UnixEpoch.AddDays(index),
                [],
                FileLink(id),
                index + 1 < FileIds.Length ? File(FileIds[index + 1]) : null)),
        ]);

    /// <summary>
    /// Says what changed in 1.2 and 1.3.
    /// </summary>
    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModChangelog>>(
            [new ModChangelog("1.2", ["Smaller."]), new ModChangelog("1.3", ["Faster.", "Fixed the crash."])]);

    private static ModFileRef File(string fileId) => new(new ModRef(new GameRef(SourceId, "game"), "mod"), fileId);
}
