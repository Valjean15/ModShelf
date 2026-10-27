using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// The library: the mods the user watches for updates, has downloaded files of, or both.
/// </summary>
public interface IModLibrary
{
    /// <returns>Every mod in the library, in the order they were added.</returns>
    Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken);

    /// <returns>The mod as the library has it, or null when it isn't in the library.</returns>
    Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Watches the mod a link is to, or to one of whose files, for updates: adds it to the library, or watches it again.
    /// Its name, author and page are read from its site, so they're up to date either way.
    /// </summary>
    /// <returns>The mod as the library has it now.</returns>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link, or its site can't describe its mods.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Stops watching the mod a link is to. A mod with downloaded files stays in the library; one without leaves it.
    /// Unwatching a mod that isn't watched changes nothing.
    /// </summary>
    /// <inheritdoc cref="WatchAsync" path="/exception"/>
    Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken);
}
