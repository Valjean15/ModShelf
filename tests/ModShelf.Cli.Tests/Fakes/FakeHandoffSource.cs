using System.Collections.Concurrent;
using System.Net;
using System.Web;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A site that, as Nexus Mods does for free accounts, lets a file be downloaded only once the user has started the
/// download on its website: until it gets a handoff link with the right key, it asks for one. Its files are at
/// <c>https://handoff.example/files/{id}</c>, its handoff links are <c>fakehandoff://files/{id}?key=…</c>, and every
/// file downloads from <paramref name="fileUrl"/>. Links under <c>/locked/</c> need an account it doesn't have, and
/// links under <c>/missing/</c> are to files it doesn't have.
/// </summary>
internal sealed class FakeHandoffSource(Uri fileUrl) : ILinkRecognizer, IDownloadResolver
{
    public const string GoodKey = "handoff-key-3f9a1c";

    private const string Host = "handoff.example";
    private const string Scheme = "fakehandoff";

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

    private static ModFileRef File(string fileId) => new(new ModRef(new GameRef(SourceId, "game"), "mod"), fileId);
}
