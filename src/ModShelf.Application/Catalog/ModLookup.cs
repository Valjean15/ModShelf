using System.Collections.Concurrent;

using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Finds the source a link belongs to, as <see cref="Downloads.ILinkDownloads"/> does, and asks its <see cref="IModCatalog"/>
/// about the mod, all four questions at once. Searches go to the game's source's <see cref="IModSearch"/>. Each
/// source's games are kept once read.
/// </summary>
internal sealed class ModLookup : IModLookup
{
    private readonly ISourceRegistry _sources;

    // Only lists that were read: a failed read is tried again. Two first reads at once both ask, and either list is kept.
    private readonly ConcurrentDictionary<SourceId, IReadOnlyList<GameInfo>> _games = new();

    public ModLookup(ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    public IReadOnlyList<SourceId> SearchableSources => [.. _sources.All<IModSearch>().Select(source => source.Id)];

    public async Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken)
    {
        (RecognizedLink recognized, IModCatalog catalog) = await _sources.RecognizeForAsync<IModCatalog>(
                link, "This link isn't to a mod on a site ModShelf can look mods up on.", cancellationToken)
            .ConfigureAwait(false);
        return await DescribeAsync(catalog, recognized.File.Mod, recognized.File, cancellationToken).ConfigureAwait(false);
    }

    public Task<ModPage> LookUpAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        IModCatalog catalog = _sources.Require<IModCatalog>(modRef.Game.Source, "can't describe its mods", nameof(modRef));
        return DescribeAsync(catalog, modRef, linkedFile: null, cancellationToken);
    }

    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        IModCatalog catalog = _sources.Require<IModCatalog>(source, "can't describe its games", nameof(source));
        if (_games.TryGetValue(source, out IReadOnlyList<GameInfo>? kept))
        {
            return kept;
        }

        IReadOnlyList<GameInfo> games = await catalog.GetGamesAsync(cancellationToken).ConfigureAwait(false);
        return _games.GetOrAdd(source, games);
    }

    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(query);

        IModSearch search = _sources.Require<IModSearch>(game.Source, "can't search", nameof(game));
        return search.SearchAsync(game, query, cancellationToken);
    }

    /// <summary>
    /// Asks the catalog the four questions about the mod at once.
    /// </summary>
    private static async Task<ModPage> DescribeAsync(IModCatalog catalog, ModRef mod, ModFileRef? linkedFile, CancellationToken cancellationToken)
    {
        Task<GameInfo> game = catalog.GetGameAsync(mod.Game, cancellationToken);
        Task<ModInfo> info = catalog.GetModAsync(mod, cancellationToken);
        Task<IReadOnlyList<ModFile>> files = catalog.GetFilesAsync(mod, cancellationToken);
        Task<IReadOnlyList<ModChangelog>> changelogs = catalog.GetChangelogsAsync(mod, cancellationToken);
        await Task.WhenAll(game, info, files, changelogs).ConfigureAwait(false);

        return new ModPage(
            await game.ConfigureAwait(false),
            await info.ConfigureAwait(false),
            await files.ConfigureAwait(false),
            await changelogs.ConfigureAwait(false),
            linkedFile);
    }
}
