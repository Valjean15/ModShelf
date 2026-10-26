using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Updates;

/// <summary>
/// Which of a game's mods changed lately, in one request per game rather than one per mod. It reaches back only so
/// far; changes older than that need each mod checked on its own.
/// </summary>
public interface IUpdateFeed : IModSource
{
    /// <summary>
    /// How far back the feed reaches. <see cref="GetUpdatesAsync"/> refuses an earlier <c>since</c>, but takes one up to
    /// a minute earlier still, so a caller can ask for all of it with a time read from its own clock a moment before.
    /// </summary>
    TimeSpan Reach { get; }

    /// <returns>The game's mods with any activity since <paramref name="since"/>, in no particular order.</returns>
    /// <exception cref="ArgumentException">The game isn't this source's.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="since"/> is further back than <see cref="Reach"/>.</exception>
    /// <exception cref="Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken);
}
