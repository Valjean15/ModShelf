namespace ModShelf.Application.Accounts.Ports;

/// <summary>
/// Opens web pages in the user's default browser. Implemented by Platform.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>
    /// Asks the system to open <paramref name="url"/>.
    /// </summary>
    /// <returns>False when the system couldn't open it, such as on a computer without a browser.</returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> isn't an absolute http or https URL.</exception>
    Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken);
}
