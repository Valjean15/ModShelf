using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// <see cref="FakeModSite"/>'s catalog, with an update feed reaching back 28 days, by the test's clock. The feed lists
/// the changes the test gives it that are recent enough, and records each question. It can fail as the test says.
/// </summary>
internal sealed class FakeModSiteWithFeed(FakeModSite site, TimeProvider timeProvider) : IModCatalog, IUpdateFeed
{
    private readonly ConcurrentQueue<(GameRef Game, DateTimeOffset Since)> _questions = new();

    public SourceId Id => site.Id;

    public string DisplayName => site.DisplayName;

    public TimeSpan Reach => TimeSpan.FromDays(28);

    public FakeModSite Site => site;

    /// <summary>
    /// The mods whose files or pages changed, as the feed lists them.
    /// </summary>
    public List<ModUpdate> Changes { get; } = [];

    /// <summary>
    /// What the next question to the feed throws instead.
    /// </summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<(GameRef Game, DateTimeOffset Since)> Questions => [.. _questions];

    public static ModUpdate Changed(string modId, DateTimeOffset activity, DateTimeOffset? files)
        => new(FakeModSite.Mod(modId), activity, files, null);

    public Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        _questions.Enqueue((game, since));
        if (Failure is { } failure)
        {
            Failure = null;
            return Task.FromException<IReadOnlyList<ModUpdate>>(failure);
        }

        if (since < timeProvider.GetUtcNow() - Reach - TimeSpan.FromMinutes(1))
        {
            return Task.FromException<IReadOnlyList<ModUpdate>>(new ArgumentOutOfRangeException(nameof(since), "Too far back."));
        }

        return Task.FromResult<IReadOnlyList<ModUpdate>>([.. Changes.Where(change => change.Mod.Game == game && change.LastActivityAt >= since)]);
    }

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => site.GetGameAsync(game, cancellationToken);

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetModAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetFilesAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => site.GetChangelogsAsync(modRef, cancellationToken);
}
