namespace ModShelf.Application.Accounts.Ports;

/// <summary>
/// Receives the browser's return from an OAuth authorization page: the loopback redirect that desktop apps
/// use, as RFC 8252 describes. Implemented by Platform.
/// </summary>
public interface IOAuthRedirectListener
{
    /// <summary>
    /// Starts listening on 127.0.0.1, on a port the system picks, until the returned redirect is disposed.
    /// </summary>
    Task<IOAuthRedirect> StartAsync(CancellationToken cancellationToken);
}
