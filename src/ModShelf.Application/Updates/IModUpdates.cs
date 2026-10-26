using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Which of a game's mods changed lately, from the update feed of the game's source: one request per game.
/// </summary>
public interface IModUpdates
{
    /// <summary>
    /// The sources with an update feed, by ID.
    /// </summary>
    IReadOnlyList<SourceId> Sources { get; }

    /// <returns>The game's mods with any activity since <paramref name="since"/>, the most recently active first.</returns>
    /// <exception cref="ArgumentException">
    /// The game's source isn't registered or has no update feed, or the source doesn't know the game.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="since"/> is further back than the feed reaches.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken);
}
