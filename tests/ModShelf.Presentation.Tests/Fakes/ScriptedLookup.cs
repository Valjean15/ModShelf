using System.Collections.Concurrent;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Mod sites that list their games, search their mods and describe them as the test says, recording what each was asked.
/// The sites of <see cref="Others"/> answer as the app's own lookup has them answer.
/// </summary>
internal sealed class ScriptedLookup : IModLookup
{
    private readonly ConcurrentQueue<string> _asked = new();

    public List<SourceId> Searchable { get; } = [];

    /// <summary>
    /// Sites the test doesn't script, such as the example site: asked through the app's own lookup, which asks them.
    /// </summary>
    public IModLookup? Others { get; init; }

    public IReadOnlyList<SourceId> SearchableSources => [.. Searchable, .. Others?.SearchableSources ?? []];

    /// <summary>
    /// Each site's games.
    /// </summary>
    public Dictionary<SourceId, IReadOnlyList<GameInfo>> Games { get; } = [];

    /// <summary>
    /// The sites that find their games by name, as GameBanana does: what <see cref="Games"/> has for one is what its
    /// search finds, matched as the app matches names.
    /// </summary>
    public HashSet<SourceId> ByName { get; } = [];

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
    /// What changed in each mod's versions, by the mod and the version's label.
    /// </summary>
    public Dictionary<(ModRef Mod, string Version), IReadOnlyList<string>> Changes { get; } = [];

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
    /// Whether the adult mods a search finds are left out of its page unless asked for, as the app does for a site that
    /// marks them but can't leave them out, which the page says.
    /// </summary>
    public bool LeavesOutAdultMods { get; set; }

    /// <summary>
    /// What was asked, in order, such as "GetGamesAsync nexus" or "LookUpAsync 12604".
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    public Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"LookUpAsync {link}");
        if (Others is not null && !LinkedPages.ContainsKey(link))
        {
            return Others.LookUpAsync(link, cancellationToken);
        }

        return Answer(nameof(LookUpAsync), () => LinkedPages.TryGetValue(link, out ModPage? page)
            ? page
            : throw new UnsupportedLinkException("No source knows this link."));
    }

    public Task<ModPage> LookUpAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"LookUpAsync {modRef.Id}");
        if (IsOthers(modRef.Game.Source))
        {
            return Others!.LookUpAsync(modRef, cancellationToken);
        }

        return Answer(nameof(LookUpAsync), () => Pages.TryGetValue(modRef, out ModPage? page)
            ? page
            : throw new HttpRequestException("No Mod Found", null, System.Net.HttpStatusCode.NotFound));
    }

    public Task<IReadOnlyList<string>?> GetChangesAsync(ModRef modRef, string version, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"GetChangesAsync {modRef.Id} {version}");
        if (IsOthers(modRef.Game.Source))
        {
            return Others!.GetChangesAsync(modRef, version, cancellationToken);
        }

        return Answer(nameof(GetChangesAsync), () => (IReadOnlyList<string>?)(Changes.GetValueOrDefault((modRef, version)) ?? []));
    }

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(SourceId source, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"GetGamesAsync {source}");
        if (IsOthers(source))
        {
            return Others!.GetGamesAsync(source, cancellationToken);
        }

        return Answer(nameof(GetGamesAsync), () => Games.GetValueOrDefault(source) ?? []);
    }

    public bool FindsGamesByName(SourceId source) => IsOthers(source) ? Others!.FindsGamesByName(source) : ByName.Contains(source);

    public Task<IReadOnlyList<GameInfo>> FindGamesAsync(SourceId source, NameQuery query, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"FindGamesAsync {source} {query.Text}");
        if (IsOthers(source))
        {
            return Others!.FindGamesAsync(source, query, cancellationToken);
        }

        return Answer(nameof(FindGamesAsync), () => ByName.Contains(source) && query.IsEmpty
            ? throw new ArgumentException("Give words of the game's name.", nameof(query))
            : query.Find((Games.GetValueOrDefault(source) ?? []).OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase), game => game.Name));
    }

    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"SearchAsync {game.Key} {query.Offset}");
        lock (Searches)
        {
            Searches.Add((game, query));
        }

        if (IsOthers(game.Source))
        {
            return await Others!.SearchAsync(game, query, cancellationToken);
        }

        if (HeldSearches.TryDequeue(out TaskCompletionSource? hold))
        {
            await hold.Task;
        }

        List<ModInfo> mods =
        [
            .. (Mods.GetValueOrDefault(game) ?? []).Where(mod => query.Text is null || mod.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase)),
        ];
        ModInfo[] page = [.. mods.Skip(query.Offset).Take(query.Count)];
        ModInfo[] kept = LeavesOutAdultMods && !query.IncludeAdultContent ? [.. page.Where(mod => !mod.HasAdultContent)] : page;
        return await Answer(nameof(SearchAsync), () => new SearchPage(kept, mods.Count, query.Offset, page.Length - kept.Length));
    }

    private bool IsOthers(SourceId source) => Others?.SearchableSources.Contains(source) == true && !Searchable.Contains(source);

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
