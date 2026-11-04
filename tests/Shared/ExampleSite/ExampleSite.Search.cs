using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Testing;

public sealed partial class ExampleSite
{
    /// <summary>
    /// The orders the site has; it refuses the rest.
    /// </summary>
    public static IReadOnlyList<SearchSort> Sorts { get; } =
        [SearchSort.Relevance, SearchSort.Downloads, SearchSort.Updated, SearchSort.Newest, SearchSort.Name];

    /// <summary>
    /// Finds a game's mods whose names hold every word, by the orders in <see cref="Sorts"/>, and how recently they were
    /// updated; at most <see cref="PageLimit"/> a page. It has no authors, categories or adult content to filter by, so
    /// it refuses a search by author or category. The best matches are the names that start with the words.
    /// </summary>
    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(query);
        EnsureOwn(game, nameof(game));
        if (!Sorts.Contains(query.Sort))
        {
            throw new ArgumentException($"Example Mods can't sort by {query.Sort}.", nameof(query));
        }

        if (query.Author is not null || query.Category is not null)
        {
            throw new ArgumentException("Example Mods can't search by author or category.", nameof(query));
        }

        if (game != StardewValley.Game && game != PixelFarm.Game)
        {
            throw NotFound($"game {game.Key}");
        }

        string[] words = query.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        ExampleMod[] mods;
        lock (_gate)
        {
            mods = [.. _mods.Values.Where(mod => mod.Mod.Game == game)];
        }

        ModInfo[] found =
        [
            .. mods
                .Where(mod => words.All(word => mod.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(Describe)
                .Where(mod => query.UpdatedSince is not { } since || mod.UpdatedAt >= since),
        ];
        IEnumerable<ModInfo> ordered = query.Sort switch
        {
            SearchSort.Downloads => found.OrderByDescending(mod => mod.Downloads),
            SearchSort.Updated => found.OrderByDescending(mod => mod.UpdatedAt),
            SearchSort.Newest => found.OrderByDescending(mod => mod.PublishedAt),
            SearchSort.Name => found.OrderByDescending(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
            _ => found
                .OrderByDescending(mod => words.Length > 0 && mod.Name.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(mod => mod.Downloads),
        };
        if (query.Direction == SearchDirection.Ascending)
        {
            ordered = ordered.Reverse();
        }

        ModInfo[] page = [.. ordered.Skip(query.Offset).Take(Math.Min(query.Count, PageLimit))];
        return Task.FromResult(new SearchPage(page, found.Length, query.Offset));
    }
}
