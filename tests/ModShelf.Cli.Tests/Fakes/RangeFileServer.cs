using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A small HTTP/1.1 server on 127.0.0.1 that serves one file and continues it from <c>Range: bytes=N-</c>, as file
/// hosts do. It can stop sending part-way through the file and keep the connection open, like a stuck connection,
/// so a test can interrupt a download at a known byte.
/// </summary>
internal sealed class RangeFileServer : IAsyncDisposable
{
    private const int ChunkSize = 64 * 1024;
    private const int MaxRequestHeadLength = 16 * 1024;
    private const long NoHold = -1;

    private readonly byte[] _content;
    private readonly string _fileName;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<string?> _ranges = new();
    private readonly Task _accepting;
    private long _holdAt = NoHold;

    public RangeFileServer(byte[] content, string fileName)
    {
        _content = content;
        _fileName = fileName;
        _listener.Start();
        _accepting = AcceptAsync(_stopping.Token);
    }

    public Uri FileUrl => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{_fileName}");

    /// <summary>
    /// Each request's <c>Range</c> header, in order; null for a request without one.
    /// </summary>
    public IReadOnlyList<string?> Ranges => [.. _ranges];

    /// <summary>
    /// From now on, responses stop once the file's first <paramref name="offset"/> bytes are out, and hold the
    /// connection open until the client goes away.
    /// </summary>
    public void HoldAt(long offset) => Interlocked.Exchange(ref _holdAt, offset);

    /// <summary>
    /// From now on, responses send the rest of the file.
    /// </summary>
    public void SendEverything() => Interlocked.Exchange(ref _holdAt, NoHold);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        _listener.Dispose();
        _stopping.Dispose();
    }

    private static async Task<string?> ReadRequestHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestHeadLength];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
            string head = Encoding.ASCII.GetString(buffer, 0, length);
            int end = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return head[..end];
            }
        }

        return null;
    }

    private static string? HeaderValue(string[] lines, string name)
    {
        string prefix = name + ":";
        string? line = lines.Skip(1).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line?[prefix.Length..].Trim();
    }

    private static async Task WriteHeadAsync(NetworkStream stream, string status, IEnumerable<string> headers, CancellationToken cancellationToken)
    {
        string head = $"HTTP/1.1 {status}\r\n{string.Concat(headers.Select(header => header + "\r\n"))}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the client to close the connection, or the server to stop.
    /// </summary>
    private static async Task HoldAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] probe = new byte[1];
        while (await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
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
                string? head = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (head is not null)
                {
                    await RespondAsync(stream, head.Split("\r\n"), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // The client went away, such as a killed CLI, or the server is stopping.
            }
        }
    }

    private async Task RespondAsync(NetworkStream stream, string[] lines, CancellationToken cancellationToken)
    {
        string[] requestLine = lines[0].Split(' ');
        if (requestLine is not ["GET", { } target, _] || target != "/" + _fileName)
        {
            await WriteHeadAsync(stream, "404 Not Found", ["Content-Length: 0"], cancellationToken).ConfigureAwait(false);
            return;
        }

        string? range = HeaderValue(lines, "Range");
        _ranges.Enqueue(range);

        long total = _content.Length;
        long start = 0;
        if (range is not null)
        {
            if (!range.StartsWith("bytes=", StringComparison.Ordinal)
                || !range.EndsWith('-')
                || !long.TryParse(range["bytes=".Length..^1], NumberStyles.None, CultureInfo.InvariantCulture, out start)
                || start >= total)
            {
                await WriteHeadAsync(stream, "416 Range Not Satisfiable", [$"Content-Range: bytes */{total}", "Content-Length: 0"], cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        string[] headers =
        [
            "Content-Type: application/octet-stream",
            "Accept-Ranges: bytes",
            $"Content-Length: {total - start}",
            .. range is null ? Array.Empty<string>() : [$"Content-Range: bytes {start}-{total - 1}/{total}"],
        ];
        await WriteHeadAsync(stream, range is null ? "200 OK" : "206 Partial Content", headers, cancellationToken).ConfigureAwait(false);

        long holdAt = Interlocked.Read(ref _holdAt);
        long end = holdAt == NoHold ? total : Math.Clamp(holdAt, start, total);
        for (long position = start; position < end; position += ChunkSize)
        {
            int count = (int)Math.Min(ChunkSize, end - position);
            await stream.WriteAsync(_content.AsMemory((int)position, count), cancellationToken).ConfigureAwait(false);
        }

        if (end < total)
        {
            await HoldAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }
}
