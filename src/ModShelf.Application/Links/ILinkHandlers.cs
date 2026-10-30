using ModShelf.Domain.Mods;

namespace ModShelf.Application.Links;

/// <summary>
/// Which app opens the links the sites hand their downloads over with, such as Nexus Mods' <c>nxm://</c> links, and
/// making ModShelf open them, or giving them back.
/// </summary>
public interface ILinkHandlers
{
    /// <summary>
    /// Each registered source that hands its downloads over with links of its own scheme, in the order they're
    /// registered.
    /// </summary>
    Task<IReadOnlyList<LinkHandler>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes the system open <paramref name="source"/>'s links with ModShelf, remembering the app that opened them
    /// before.
    /// </summary>
    /// <returns>The site's links as the system opens them now: still with another app the user chose in the system's
    /// own settings, if so.</returns>
    /// <exception cref="ArgumentException">The source isn't registered, or hands nothing over with links of its own.</exception>
    /// <exception cref="InvalidOperationException">This ModShelf can't be registered here.</exception>
    /// <exception cref="IOException">The system's register couldn't be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The system's register couldn't be changed.</exception>
    Task<LinkHandler> TakeAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the system opening <paramref name="source"/>'s links with ModShelf, and gives them back to the app that
    /// opened them before, if any.
    /// </summary>
    /// <returns>The site's links as the system opens them now.</returns>
    /// <exception cref="ArgumentException">The source isn't registered, or hands nothing over with links of its own.</exception>
    /// <exception cref="IOException">The system's register couldn't be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The system's register couldn't be changed.</exception>
    Task<LinkHandler> GiveBackAsync(SourceId source, CancellationToken cancellationToken);
}
