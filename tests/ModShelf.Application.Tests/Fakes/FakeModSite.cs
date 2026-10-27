using System.Collections.Concurrent;
using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site whose mods the test adds, all of one game. It recognizes <c>https://mods.example/{mod}/{file}</c>, and
/// <c>fakemods://{mod}/{file}</c> as a link that completes a handoff, and suggests no file name, so what its catalog
/// says counts. The test can make any answer fail, or hold the answers back. It records what it was asked.
/// </summary>
internal sealed class FakeModSite : ILinkRecognizer, IModCatalog
{
    private readonly ConcurrentDictionary<string, string> _mods = new();
    private readonly ConcurrentDictionary<string, List<ModFile>> _files = new();
    private readonly ConcurrentQueue<string> _asked = new();

    public static SourceId SourceId { get; } = new("fake-mods");

    public static GameRef Game { get; } = new(SourceId, "skyrimspecialedition");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake mod site";

    /// <summary>
    /// What the next answer of each kind throws instead, keyed by the method's name.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new();

    /// <summary>
    /// While set, every catalog answer waits for it.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// The methods called so far, in order.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public static ModRef Mod(string modId) => new(Game, modId);

    public static ModFileRef File(string modId, string fileId) => new(Mod(modId), fileId);

    public static Uri Link(string modId, string fileId) => new($"https://mods.example/{modId}/{fileId}");

    public static Uri HandoffLink(string modId, string fileId) => new($"fakemods://{modId}/{fileId}?key=k");

    public void AddMod(string modId, string name) => _mods[modId] = name;

    public void AddFile(string modId, string fileId, string fileName, string name = "Main file", string? version = "1.0")
        => _files.GetOrAdd(modId, _ => []).Add(
            new ModFile(File(modId, fileId), name, fileName, version, ModFileCategory.Main, 100, TestData.Now.AddDays(-1), []));

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        _asked.Enqueue(nameof(RecognizeAsync));
        bool isHandoff = link.Scheme == "fakemods";
        if (!isHandoff && link.Host != "mods.example")
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        string[] parts = isHandoff ? [link.Host, link.AbsolutePath.Trim('/')] : link.AbsolutePath.Trim('/').Split('/');
        return Task.FromResult<RecognizedLink?>(new RecognizedLink(File(parts[0], parts[1]), suggestedFileName: null, isHandoff: isHandoff));
    }

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetGameAsync), () => new GameInfo(game, "Skyrim Special Edition"));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetModAsync), () => _mods.TryGetValue(modRef.Id, out string? name)
            ? new ModInfo(modRef, name, author: "Someone", pageUrl: new Uri($"https://mods.example/{modRef.Id}"))
            : throw new HttpRequestException("No such mod.", null, HttpStatusCode.NotFound));

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetFilesAsync), () => (IReadOnlyList<ModFile>)[.. _files.GetValueOrDefault(modRef.Id) ?? []]);

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetChangelogsAsync), () => (IReadOnlyList<ModChangelog>)[]);

    private async Task<T> AnswerAsync<T>(string method, Func<T> answer)
    {
        _asked.Enqueue(method);
        if (Hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return Failures.TryRemove(method, out Exception? failure) ? throw failure : answer();
    }
}
