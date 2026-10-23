using System.Net;
using System.Net.Sockets;

using ModShelf.Application.Accounts;

namespace ModShelf.Platform.OAuth;

/// <summary>
/// Listens for OAuth redirects on 127.0.0.1, on a port the system picks, as RFC 8252 recommends for desktop apps.
/// </summary>
/// <remarks>
/// A plain TCP listener with a minimal HTTP reply rather than <c>HttpListener</c>, which on Windows needs URL
/// reservations. Only this computer can connect, and the strategy checks the OAuth state of whatever arrives.
/// </remarks>
public sealed class LoopbackRedirectListener : IOAuthRedirectListener
{
    public Task<IOAuthRedirect> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return Task.FromResult<IOAuthRedirect>(new LoopbackRedirect(listener, new Uri($"http://127.0.0.1:{port}/")));
    }
}
