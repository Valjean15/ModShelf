using System.Collections.Concurrent;
using System.Globalization;

using ModShelf.Sources.CurseForge.Api;

namespace ModShelf.Sources.CurseForge.Catalog;

/// <summary>
/// CurseForge's games, read once a run, and each one's classes, such as Minecraft's Mods and Resource Packs, read once
/// for each game asked about: a game's slug, its ID and its classes don't change while ModShelf runs (ADR 0107).
/// </summary>
internal sealed class CurseForgeGames(CurseForgeApiClient api) : IDisposable
{
    // A page of games, the most CurseForge gives.
    private const int PageSize = 50;

    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly ConcurrentDictionary<long, IReadOnlyList<CurseForgeCategory>> _classes = new();
    private IReadOnlyList<CurseForgeGame>? _games;

    public void Dispose() => _reading.Dispose();

    /// <summary>
    /// Every game, as CurseForge lists them.
    /// </summary>
    public async Task<IReadOnlyList<CurseForgeGame>> AllAsync(CancellationToken cancellationToken)
    {
        if (_games is { } read)
        {
            return read;
        }

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_games is { } meanwhile)
            {
                return meanwhile;
            }

            List<CurseForgeGame> games = [];
            for (int index = 0; ; index += PageSize)
            {
                CurseForgeAnswer<IReadOnlyList<CurseForgeGame>> page = await api
                    .GetAsync<IReadOnlyList<CurseForgeGame>>(string.Create(CultureInfo.InvariantCulture, $"v1/games?index={index}&pageSize={PageSize}"), cancellationToken)
                    .ConfigureAwait(false);
                IReadOnlyList<CurseForgeGame> found = page.Data ?? [];
                games.AddRange(found.Where(game => !string.IsNullOrWhiteSpace(game.Slug) && !string.IsNullOrWhiteSpace(game.Name)));
                if (found.Count < PageSize || page.Pagination is not { } pagination || index + found.Count >= pagination.TotalCount)
                {
                    break;
                }
            }

            _games = games;
            return games;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>
    /// The game whose slug is <paramref name="slug"/>, in any case; null for none.
    /// </summary>
    public async Task<CurseForgeGame?> BySlugAsync(string slug, CancellationToken cancellationToken)
        => (await AllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(game => string.Equals(game.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The game whose ID is <paramref name="id"/>; null for none.
    /// </summary>
    public async Task<CurseForgeGame?> ByIdAsync(long id, CancellationToken cancellationToken)
        => (await AllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(game => game.Id == id);

    /// <summary>
    /// The game's classes, such as Mods and Resource Packs.
    /// </summary>
    public async Task<IReadOnlyList<CurseForgeCategory>> ClassesAsync(long game, CancellationToken cancellationToken)
    {
        if (_classes.TryGetValue(game, out IReadOnlyList<CurseForgeCategory>? classes))
        {
            return classes;
        }

        CurseForgeAnswer<IReadOnlyList<CurseForgeCategory>> answer = await api
            .GetAsync<IReadOnlyList<CurseForgeCategory>>(string.Create(CultureInfo.InvariantCulture, $"v1/categories?gameId={game}&classesOnly=true"), cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<CurseForgeCategory> read = [.. (answer.Data ?? []).Where(found => !string.IsNullOrWhiteSpace(found.Name))];
        return _classes.GetOrAdd(game, read);
    }
}
