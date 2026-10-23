using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Resources;

namespace ModShelf.Platform.OAuth;

/// <summary>
/// One listening redirect. Answers the browser's request for the redirect URI with a page telling the user to
/// go back to ModShelf, and anything else, such as <c>/favicon.ico</c>, with 404.
/// </summary>
internal sealed class LoopbackRedirect(TcpListener listener, Uri redirectUri) : IOAuthRedirect
{
    private const int MaxRequestHeadBytes = 16 * 1024;

    // A browser sends its request at once; a connection that stays silent this long is dropped.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public Uri RedirectUri { get; } = redirectUri;

    public async Task<Uri> WaitForRedirectAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            Uri? redirect = await HandleAsync(client, cancellationToken).ConfigureAwait(false);
            if (redirect is not null)
            {
                return redirect;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        listener.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <returns>The target of a GET request, such as <c>/?code=…&amp;state=…</c>; null for anything else.</returns>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestHeadBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        string head = Encoding.ASCII.GetString(buffer, 0, length);
        string[] requestLine = head.Split("\r\n", 2)[0].Split(' ');
        return requestLine is ["GET", var target, var version] && target.StartsWith('/') && version.StartsWith("HTTP/", StringComparison.Ordinal)
            ? target
            : null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static string Page()
        => $"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(Strings.RedirectPageTitle)}</title></head>
            <body style="font-family: sans-serif; text-align: center; margin-top: 20vh;"><p>{WebUtility.HtmlEncode(Strings.RedirectPageMessage)}</p></body></html>
            """;

    /// <returns>The redirect, or null for any other request, which gets an error reply.</returns>
    private async Task<Uri?> HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        NetworkStream stream = client.GetStream();
        try
        {
            string? target = await ReadRequestTargetAsync(stream, timeout.Token).ConfigureAwait(false);
            if (target is null || !string.Equals(target.Split('?', 2)[0], RedirectUri.AbsolutePath, StringComparison.Ordinal))
            {
                await RespondAsync(stream, "404 Not Found", string.Empty, timeout.Token).ConfigureAwait(false);
                return null;
            }

            await RespondAsync(stream, "200 OK", Page(), timeout.Token).ConfigureAwait(false);
            return new Uri(RedirectUri, target);
        }
        catch (Exception exception) when (exception is IOException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A connection that broke off or stayed silent; keep waiting for the browser.
            return null;
        }
    }
}
