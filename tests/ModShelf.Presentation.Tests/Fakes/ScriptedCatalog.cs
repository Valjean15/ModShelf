using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A mod site that names its games, and gives its mods' details and changelogs, as the test says, recording what it was
/// asked.
/// </summary>
internal sealed class ScriptedCatalog(SourceId id, string displayName) : IModCatalog
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    /// <summary>
    /// Each game's name by its key; a game not here is unknown to the site.
    /// </summary>
    public ConcurrentDictionary<string, string> GameNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Each mod's changelog by its ID; a mod not here has an empty one.
    /// </summary>
    public ConcurrentDictionary<string, IReadOnlyList<ModChangelog>> Changelogs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Each mod's details by its ID; a mod not here doesn't exist.
    /// </summary>
    public ConcurrentDictionary<string, ModInfo> Mods { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// What asking the site throws, such as a refused account; null for nothing.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// When set, naming a game waits for it.
    /// </summary>
    public TaskCompletionSource? HoldGames { get; set; }

    /// <summary>
    /// What was asked, in order, such as "GetGameAsync skyrimspecialedition".
    /// </summary>
    public ConcurrentQueue<string> Asked { get; } = new();

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"GetGameAsync {game.Key}");
        if (Refusal is not null)
        {
            return Task.FromException<GameInfo>(Refusal);
        }

        return HoldGames is { } hold ? NameAfterAsync(hold.Task, game) : Name(game);
    }

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"GetModAsync {modRef.Id}");
        if (Refusal is not null)
        {
            return Task.FromException<ModInfo>(Refusal);
        }

        return Mods.TryGetValue(modRef.Id, out ModInfo? mod)
            ? Task.FromResult(mod)
            : Task.FromException<ModInfo>(new HttpRequestException("No Mod Found", null, System.Net.HttpStatusCode.NotFound));
    }

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"GetChangelogsAsync {modRef.Id}");
        if (Refusal is not null)
        {
            return Task.FromException<IReadOnlyList<ModChangelog>>(Refusal);
        }

        return Task.FromResult(Changelogs.TryGetValue(modRef.Id, out IReadOnlyList<ModChangelog>? changelogs) ? changelogs : []);
    }

    private Task<GameInfo> Name(GameRef game)
        => GameNames.TryGetValue(game.Key, out string? name)
            ? Task.FromResult(new GameInfo(game, name))
            : Task.FromException<GameInfo>(new HttpRequestException("No such game.", null, System.Net.HttpStatusCode.NotFound));

    private async Task<GameInfo> NameAfterAsync(Task hold, GameRef game)
    {
        await hold;
        return await Name(game);
    }
}
