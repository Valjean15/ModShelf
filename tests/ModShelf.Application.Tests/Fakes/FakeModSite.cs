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
    private readonly ConcurrentQueue<string> _askedFor = new();

    public static SourceId SourceId { get; } = new("fake-mods");

    public static GameRef Game { get; } = new(SourceId, "skyrimspecialedition");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake mod site";

    /// <summary>
    /// What the next answer of each kind throws instead, keyed by the method's name.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new();

    /// <summary>
    /// What every answer about a mod throws instead, by the mod's ID, until it's removed.
    /// </summary>
    public ConcurrentDictionary<string, Exception> ModFailures { get; } = new();

    /// <summary>
    /// While set, every catalog answer waits for it.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// The methods called so far, in order.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    /// <summary>
    /// The catalog's answers so far, in order, each as the method's name and the mod's ID, such as "GetFilesAsync 12604".
    /// </summary>
    public IReadOnlyList<string> AskedFor => [.. _askedFor];

    public static ModRef Mod(string modId) => new(Game, modId);

    public static ModFileRef File(string modId, string fileId) => new(Mod(modId), fileId);

    public static Uri Link(string modId, string fileId) => new($"https://mods.example/{modId}/{fileId}");

    public static Uri HandoffLink(string modId, string fileId) => new($"fakemods://{modId}/{fileId}?key=k");

    public void AddMod(string modId, string name) => _mods[modId] = name;

    /// <param name="replacedBy">The ID of the newer file of the mod that replaced this one, if any.</param>
    /// <param name="uploadedAt">When it was uploaded; a day before <see cref="TestData.Now"/> unless said.</param>
    public void AddFile(
        string modId,
        string fileId,
        string fileName,
        string name = "Main file",
        string? version = "1.0",
        string? replacedBy = null,
        ModFileCategory category = ModFileCategory.Main,
        DateTimeOffset? uploadedAt = null)
        => _files.GetOrAdd(modId, _ => []).Add(new ModFile(
            File(modId, fileId),
            name,
            fileName,
            version,
            category,
            100,
            uploadedAt ?? TestData.Now.AddDays(-1),
            [],
            new Uri($"https://mods.example/{modId}/{fileId}"),
            replacedBy: replacedBy is null ? null : File(modId, replacedBy)));

    /// <summary>
    /// Takes a file off the mod's list, as when its author deletes it.
    /// </summary>
    public void RemoveFile(string modId, string fileId) => _files[modId].RemoveAll(file => file.Reference.Id == fileId);

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

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetGamesAsync), null, () => (IReadOnlyList<GameInfo>)[new GameInfo(Game, "Skyrim Special Edition")]);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetGameAsync), null, () => new GameInfo(game, "Skyrim Special Edition"));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetModAsync), modRef, () => _mods.TryGetValue(modRef.Id, out string? name)
            ? new ModInfo(modRef, name, author: "Someone", pageUrl: new Uri($"https://mods.example/{modRef.Id}"))
            : throw new HttpRequestException("No such mod.", null, HttpStatusCode.NotFound));

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetFilesAsync), modRef, () => (IReadOnlyList<ModFile>)[.. _files.GetValueOrDefault(modRef.Id) ?? []]);

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => AnswerAsync(nameof(GetChangelogsAsync), modRef, () => (IReadOnlyList<ModChangelog>)[]);

    private async Task<T> AnswerAsync<T>(string method, ModRef? modRef, Func<T> answer)
    {
        _asked.Enqueue(method);
        _askedFor.Enqueue($"{method} {modRef?.Id}".TrimEnd());
        if (Hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        if (modRef is not null && ModFailures.TryGetValue(modRef.Id, out Exception? modFailure))
        {
            throw modFailure;
        }

        return Failures.TryRemove(method, out Exception? failure) ? throw failure : answer();
    }
}
