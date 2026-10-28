using System.Collections.Concurrent;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Mod sites that list their games, search their mods and describe them as the test says, recording what each was asked.
/// </summary>
internal sealed class ScriptedLookup : IModLookup
{
    private readonly ConcurrentQueue<string> _asked = new();

    public List<SourceId> Searchable { get; } = [];

    public IReadOnlyList<SourceId> SearchableSources => [.. Searchable];

    /// <summary>
    /// Each site's games.
    /// </summary>
    public Dictionary<SourceId, IReadOnlyList<GameInfo>> Games { get; } = [];

    /// <summary>
    /// Each game's mods, in the order the search finds them: those whose names have the words, or all without words.
    /// </summary>
    public Dictionary<GameRef, List<ModInfo>> Mods { get; } = [];

    /// <summary>
    /// Each mod's page, by the mod or by a link to it.
    /// </summary>
    public Dictionary<ModRef, ModPage> Pages { get; } = [];

    public Dictionary<Uri, ModPage> LinkedPages { get; } = [];

    /// <summary>
    /// What the next calls throw, by method name, one each.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Refusals { get; } = new();

    /// <summary>
    /// What the next searches wait for, one each, before they answer. They wait even when cancelled, as a site that
    /// answers anyway would.
    /// </summary>
    public ConcurrentQueue<TaskCompletionSource> HeldSearches { get; } = new();

    /// <summary>
    /// The searches asked for, in order.
    /// </summary>
    public List<(GameRef Game, SearchQuery Query)> Searches { get; } = [];

    /// <summary>
    /// What was asked, in order, such as "GetGamesAsync nexus" or "LookUpAsync 12604".
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"LookUpAsync {link}");
        return Answer(nameof(LookUpAsync), () => LinkedPages.TryGetValue(link, out ModPage? page)
            ? page
            : throw new UnsupportedLinkException("No source knows this link."));
    }

    public Task<ModPage> LookUpAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"LookUpAsync {modRef.Id}");
        return Answer(nameof(LookUpAsync), () => Pages.TryGetValue(modRef, out ModPage? page)
            ? page
            : throw new HttpRequestException("No Mod Found", null, System.Net.HttpStatusCode.NotFound));
    }

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(SourceId source, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"GetGamesAsync {source}");
        return Answer(nameof(GetGamesAsync), () => Games.GetValueOrDefault(source) ?? []);
    }

    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"SearchAsync {game.Key} {query.Offset}");
        lock (Searches)
        {
            Searches.Add((game, query));
        }

        if (HeldSearches.TryDequeue(out TaskCompletionSource? hold))
        {
            await hold.Task;
        }

        List<ModInfo> mods =
        [
            .. (Mods.GetValueOrDefault(game) ?? []).Where(mod => query.Text is null || mod.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase)),
        ];
        return await Answer(nameof(SearchAsync), () => new SearchPage(mods.Skip(query.Offset).Take(query.Count), mods.Count, query.Offset));
    }

    private Task<T> Answer<T>(string method, Func<T> answer)
    {
        if (Refusals.TryRemove(method, out Exception? refusal))
        {
            return Task.FromException<T>(refusal);
        }

        try
        {
            return Task.FromResult(answer());
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
    }
}
