using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site with an update feed that answers with the updates the test gives it, in that order, or fails as it says.
/// It records what it was asked.
/// </summary>
internal sealed class FakeUpdateFeed(SourceId id, params ModUpdate[] updates) : IUpdateFeed
{
    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public TimeSpan Reach => TimeSpan.FromDays(28);

    /// <summary>
    /// What the next answer throws instead.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// The game and time of the last question.
    /// </summary>
    public (GameRef Game, DateTimeOffset Since)? LastAsked { get; private set; }

    public Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        LastAsked = (game, since);
        return Failure is { } failure ? Task.FromException<IReadOnlyList<ModUpdate>>(failure) : Task.FromResult<IReadOnlyList<ModUpdate>>(updates);
    }
}
