using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

using ModShelf.Application.Accounts;
using ModShelf.Platform.OAuth;

namespace ModShelf.Platform.Tests.OAuth;

/// <summary>
/// Plays the browser with a raw TCP connection, as a browser sends a plain HTTP/1.1 request to the loopback address.
/// </summary>
public sealed class LoopbackRedirectListenerTests
{
    private readonly LoopbackRedirectListener _listener = new();

    [Fact]
    public async Task ListensOnTheLoopbackAddressOnAPortTheSystemPicked()
    {
        await using IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);
        await using IOAuthRedirect another = await _listener.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http", redirect.RedirectUri.Scheme);
        Assert.Equal("127.0.0.1", redirect.RedirectUri.Host);
        Assert.Equal("/", redirect.RedirectUri.AbsolutePath);
        Assert.NotEqual(redirect.RedirectUri.Port, another.RedirectUri.Port);
    }

    [Fact]
    public async Task OtherComputersCannotConnect()
    {
        IPAddress? networkAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
        Assert.SkipWhen(networkAddress is null, "This machine has no network address besides loopback.");
        await using IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);

        using TcpClient client = new();
        await Assert.ThrowsAsync<SocketException>(
            async () => await client.ConnectAsync(networkAddress!, redirect.RedirectUri.Port, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReturnsTheRedirectAndTellsTheUserToGoBack()
    {
        await using IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);
        Task<Uri> waiting = redirect.WaitForRedirectAsync(TestContext.Current.CancellationToken);

        string response = await BrowseAsync(redirect.RedirectUri.Port, "GET /?code=c0de&state=st4te HTTP/1.1");

        Assert.Equal(new Uri($"{redirect.RedirectUri}?code=c0de&state=st4te"), await waiting);
        Assert.StartsWith("HTTP/1.1 200 OK", response, StringComparison.Ordinal);
        Assert.Contains("go back to ModShelf", response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("POST /?code=c0de HTTP/1.1")]
    [InlineData("not http at all")]
    public async Task AnswersOtherRequestsWithNotFoundAndKeepsWaiting(string otherRequest)
    {
        await using IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);
        Task<Uri> waiting = redirect.WaitForRedirectAsync(TestContext.Current.CancellationToken);

        string other = await BrowseAsync(redirect.RedirectUri.Port, otherRequest);
        Assert.False(waiting.IsCompleted);
        await BrowseAsync(redirect.RedirectUri.Port, "GET /?code=c0de HTTP/1.1");

        Assert.StartsWith("HTTP/1.1 404", other, StringComparison.Ordinal);
        Assert.Equal("c0de", (await waiting).Query.TrimStart('?').Split('=')[1]);
    }

    [Fact]
    public async Task StopsListeningWhenDisposed()
    {
        IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);
        int port = redirect.RedirectUri.Port;

        await redirect.DisposeAsync();

        using TcpClient client = new();
        await Assert.ThrowsAsync<SocketException>(
            async () => await client.ConnectAsync("127.0.0.1", port, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitingEndsWhenCancelled()
    {
        await using IOAuthRedirect redirect = await _listener.StartAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<Uri> waiting = redirect.WaitForRedirectAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static async Task<string> BrowseAsync(int port, string requestLine)
    {
        using TcpClient client = new();
        await client.ConnectAsync("127.0.0.1", port, TestContext.Current.CancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: 127.0.0.1:{port}\r\nUser-Agent: Test\r\n\r\n");
        await stream.WriteAsync(request, TestContext.Current.CancellationToken);

        using StreamReader reader = new(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }
}
