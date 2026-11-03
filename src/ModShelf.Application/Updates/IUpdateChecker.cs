using ModShelf.Domain.Mods;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods for updates: newer files of theirs that the library doesn't have.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Checks every watched mod of the games whose updates are checked (<see cref="Settings.AppSettings.ChecksUpdatesOf"/>),
    /// and keeps the updates found on each, in place of the ones it had. Settings that can't be read mean every game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It asks as little as it can. For each game whose source has an update feed, one question to the feed covers the
    /// mods last checked within its reach, and only the mods whose files changed since then are asked about. A mod
    /// never checked, or checked longer ago, and a mod of a source without a feed, is asked about on its own.
    /// </para>
    /// <para>
    /// A game or a mod that can't be checked is a failure, and stays as it was. When a site refuses the account, or asks
    /// to slow down, its other games aren't asked about this time. One check runs at a time; another waits for it.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">The library can't be read.</exception>
    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks every watched mod of <paramref name="games"/>, whether or not their updates are checked with every game's,
    /// as <see cref="CheckAsync(CancellationToken)"/> does.
    /// </summary>
    /// <exception cref="InvalidDataException">The library can't be read.</exception>
    Task<UpdateCheck> CheckAsync(IReadOnlyCollection<GameRef> games, CancellationToken cancellationToken);
}
