namespace ModShelf.Application.Accounts.Ports;

/// <summary>
/// One listening loopback redirect from <see cref="IOAuthRedirectListener.StartAsync"/>. Disposing it stops
/// listening.
/// </summary>
public interface IOAuthRedirect : IAsyncDisposable
{
    /// <summary>
    /// The address to give the authorization page as its redirect URI, such as <c>http://127.0.0.1:49152/</c>.
    /// </summary>
    Uri RedirectUri { get; }

    /// <summary>
    /// Waits for the browser to come back, answers it with a page telling the user to return to ModShelf,
    /// and returns the full URI the browser requested, query included. Secret: never log it.
    /// </summary>
    Task<Uri> WaitForRedirectAsync(CancellationToken cancellationToken);
}
