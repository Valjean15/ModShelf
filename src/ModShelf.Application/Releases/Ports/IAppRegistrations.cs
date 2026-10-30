namespace ModShelf.Application.Releases.Ports;

/// <summary>
/// What ModShelf registers with the system for itself, outside its own folders and apart from the sites' links: on
/// Windows, the app ID its notices go by, and the <c>modshelf://</c> links a click on one opens. Other systems keep
/// nothing for it.
/// </summary>
public interface IAppRegistrations
{
    /// <summary>
    /// Removes what ModShelf registered for itself, as when it's uninstalled; nothing when it registered nothing.
    /// </summary>
    /// <exception cref="IOException">The system's register couldn't be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The system's register couldn't be changed.</exception>
    Task RemoveAsync(CancellationToken cancellationToken);
}
