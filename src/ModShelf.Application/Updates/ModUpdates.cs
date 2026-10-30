using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Passes the question to the <see cref="IUpdateFeed"/> of the game's source, and puts the answer in order.
/// </summary>
internal sealed class ModUpdates : IModUpdates
{
    private readonly ISourceRegistry _sources;

    public ModUpdates(ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    public IReadOnlyList<SourceId> Sources => [.. _sources.All<IUpdateFeed>().Select(source => source.Id)];

    public async Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        IUpdateFeed feed = _sources.Require<IUpdateFeed>(game.Source, "has no update feed", nameof(game));
        IReadOnlyList<ModUpdate> updates = await feed.GetUpdatesAsync(game, since, cancellationToken).ConfigureAwait(false);
        return [.. updates.OrderByDescending(update => update.LastActivityAt).ThenBy(update => update.Mod.Id, StringComparer.Ordinal)];
    }
}
