using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A mod site's trending mods, as the test says, recording which games were asked about.
/// </summary>
internal sealed class ScriptedTrending(SourceId id, string displayName) : ITrendingMods
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;


    public SiteWords Words => SiteWords.None;

    /// <summary>
    /// Each game's trending mods; a game not here has none.
    /// </summary>
    public ConcurrentDictionary<GameRef, IReadOnlyList<ModInfo>> Mods { get; } = new();

    /// <summary>
    /// What asking throws, such as a refused account; null for nothing.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// When set, listing waits for it.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// The games asked about, in order.
    /// </summary>
    public ConcurrentQueue<GameRef> Asked { get; } = new();

    public async Task<IReadOnlyList<ModInfo>> GetTrendingAsync(GameRef game, CancellationToken cancellationToken)
    {
        Asked.Enqueue(game);
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return Refusal is { } refusal ? throw refusal : Mods.GetValueOrDefault(game) ?? [];
    }
}
