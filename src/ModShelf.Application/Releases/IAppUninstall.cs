namespace ModShelf.Application.Releases;

/// <summary>
/// Puts back what ModShelf changed in the user's settings of the system, as its uninstaller removes it.
/// </summary>
public interface IAppUninstall
{
    /// <summary>
    /// Gives each site's links back to the app that opened them before, where ModShelf opens them or is registered for
    /// them behind an app the user chose, and removes what ModShelf registered for itself. A part that fails is logged,
    /// and the others still run.
    /// </summary>
    Task CleanUpAsync(CancellationToken cancellationToken);
}
