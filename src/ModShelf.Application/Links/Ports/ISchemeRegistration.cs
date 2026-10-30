namespace ModShelf.Application.Links.Ports;

/// <summary>
/// The system's register of which app opens a link scheme's links, such as <c>nxm://</c> links: the Windows registry,
/// the freedesktop.org default applications on Linux, and Launch Services on macOS.
/// </summary>
/// <remarks>
/// Only the scheme's own registration changes. The app that opened its links before ModShelf is remembered beside it,
/// so that giving them back puts it back, even from another ModShelf, such as when ModShelf is uninstalled.
/// </remarks>
public interface ISchemeRegistration
{
    /// <summary>
    /// Which app the system opens <paramref name="scheme"/>'s links with.
    /// </summary>
    /// <param name="scheme">Such as <c>nxm</c>: lowercase, without <c>://</c>.</param>
    Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the system open <paramref name="scheme"/>'s links with the desktop app, remembering the app that opened
    /// them before. Registering again keeps what was remembered the first time.
    /// </summary>
    /// <remarks>
    /// The system can still prefer an app the user chose in its own settings; <see cref="GetAsync"/> then names it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This ModShelf can't be registered here (<see cref="SchemeHandlerKind.Unavailable"/>).
    /// </exception>
    /// <exception cref="IOException">The system's register couldn't be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The system's register couldn't be changed.</exception>
    Task RegisterAsync(string scheme, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the system opening <paramref name="scheme"/>'s links with ModShelf, and gives them back to the app that
    /// opened them before, if any. When another app has taken them since, it keeps them. Nothing changes when ModShelf
    /// never had them.
    /// </summary>
    /// <exception cref="IOException">The system's register couldn't be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The system's register couldn't be changed.</exception>
    Task UnregisterAsync(string scheme, CancellationToken cancellationToken);
}
