using System.Collections.Specialized;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The HTTP/1.1 under the site: one request per connection, closed after the response, which is all
/// <see cref="HttpClient"/> and a browser need.
/// </summary>
internal sealed partial class FakeAccountSite
{
    private const int MaxRequestLength = 64 * 1024;

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestLength];
        int length = 0;
        int headEnd = -1;
        while (headEnd < 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0 || (length += read) == buffer.Length)
            {
                return null;
            }

            headEnd = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }

        string[] lines = Encoding.ASCII.GetString(buffer, 0, headEnd).Split("\r\n");
        if (lines[0].Split(' ') is not [{ } method, { } target, _])
        {
            return null;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        int bodyStart = headEnd + 4;
        int bodyLength = headers.TryGetValue("Content-Length", out string? declared)
            ? int.Parse(declared, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
        while (length < bodyStart + bodyLength)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        string[] pathAndQuery = target.Split('?', 2);
        NameValueCollection form = headers.TryGetValue("Content-Type", out string? type) && type.StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal)
            ? HttpUtility.ParseQueryString(Encoding.UTF8.GetString(buffer, bodyStart, bodyLength))
            : [];
        return new Request(method, pathAndQuery[0], HttpUtility.ParseQueryString(pathAndQuery.Length > 1 ? pathAndQuery[1] : string.Empty), headers, form);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, Response response, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(response.Body);
        StringBuilder head = new();
        head.Append(response.StatusLine).Append("\r\n");
        if (response.ContentType is not null)
        {
            head.Append(CultureInfo.InvariantCulture, $"Content-Type: {response.ContentType}; charset=utf-8\r\n");
        }

        if (response.Location is not null)
        {
            head.Append(CultureInfo.InvariantCulture, $"Location: {response.Location.AbsoluteUri}\r\n");
        }

        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        List<Task> connections = [];
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                connections.Add(ServeAsync(client, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping.
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                if (await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false) is not { } request)
                {
                    return;
                }

                Response response;
                lock (_gate)
                {
                    response = Handle(request);
                }

                await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // The client went away, or the site is stopping.
            }
        }
    }
}
