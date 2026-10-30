using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site with a catalog and a search. It recognizes every link as its <see cref="LinkedFile"/>, describes that file's
/// mod, and finds it in every search. The test can make any of its answers fail. It records what it was asked.
/// </summary>
internal sealed class FakeCatalogSource(SourceId id) : ILinkRecognizer, IModCatalog, IModSearch
{
    private readonly ConcurrentQueue<string> _asked = new();

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public ModFileRef LinkedFile => TestData.File(Id, "35407");

    public GameInfo Game => new(LinkedFile.Mod.Game, "Skyrim Special Edition");

    public ModInfo Mod => new(LinkedFile.Mod, "SkyUI");

    public IReadOnlyList<ModFile> Files =>
    [
        new(LinkedFile, "SkyUI 5.2", "SkyUI_5_2_SE.7z", "5.2", ModFileCategory.Main, 100, TestData.Now, []),
    ];

    public IReadOnlyList<ModChangelog> Changelogs { get; init; } = [new("5.2", ["Fixed the menus."])];

    /// <summary>
    /// Holds each changelog read until set, as a slow site does; a read whose caller gives up ends then. Null to answer at
    /// once.
    /// </summary>
    public TaskCompletionSource? ChangelogsHeld { get; init; }

    /// <summary>
    /// What the next answer of each kind throws instead, keyed by the method's name.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new();

    /// <summary>
    /// The methods called so far, in order.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
        => Answer(nameof(RecognizeAsync), () => (RecognizedLink?)new RecognizedLink(LinkedFile, "SkyUI_5_2_SE.7z"));

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => Answer(nameof(GetGamesAsync), () => (IReadOnlyList<GameInfo>)[Game]);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => Answer(nameof(GetGameAsync), () => Game);

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
        => Answer(nameof(GetModAsync), () => Mod);

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
        => Answer(nameof(GetFilesAsync), () => Files);

    public async Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        if (ChangelogsHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return await Answer(nameof(GetChangelogsAsync), () => Changelogs);
    }

    /// <summary>
    /// The game and query of the last search.
    /// </summary>
    public (GameRef Game, SearchQuery Query)? LastSearch { get; private set; }

    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        LastSearch = (game, query);
        return Answer(nameof(SearchAsync), () => new SearchPage([Mod], 1, query.Offset));
    }

    private Task<T> Answer<T>(string method, Func<T> answer)
    {
        _asked.Enqueue(method);
        return Failures.TryRemove(method, out Exception? failure) ? Task.FromException<T>(failure) : Task.FromResult(answer());
    }
}
