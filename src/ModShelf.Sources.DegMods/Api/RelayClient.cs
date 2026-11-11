using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// Reads events from a Nostr relay (NIP-01) over a WebSocket: a REQ for each filter, the events it sends until it says
/// it has sent all it holds (EOSE), each one's signature checked. A filter with a limit is read a page at a time, each
/// page until the oldest of the page before, as long as one brings events not seen yet.
/// </summary>
/// <remarks>
/// The WebSocket opens through the client the factory gives <see cref="ClientName"/>, so it goes through the same proxy
/// and retries as the source's other requests. A relay that refuses a filter (CLOSED), as DEG Mods' newer relay refuses
/// what isn't a mod, sends nothing for it, which isn't an error.
/// </remarks>
internal sealed partial class RelayClient
{
    /// <summary>
    /// The client the relays' WebSockets open through.
    /// </summary>
    internal const string ClientName = "ModShelf.Sources.DegMods.Relays";

    /// <summary>
    /// The most pages one filter is read in: past them, the rest is left unread.
    /// </summary>
    internal const int MaxPages = 50;

    /// <summary>
    /// The largest message taken from a relay, in bytes: a mod's event is a few kilobytes.
    /// </summary>
    internal const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly IHttpMessageHandlerFactory _handlers;
    private readonly TimeSpan _answerTimeout;
    private readonly string _userAgent;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RelayClient> _logger;

    public RelayClient(IHttpMessageHandlerFactory handlers, TimeSpan answerTimeout, string userAgent, TimeProvider timeProvider, ILogger<RelayClient> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(answerTimeout, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _handlers = handlers;
        _answerTimeout = answerTimeout;
        _userAgent = userAgent;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>Every genuine event the relay sent for the filters, each once, in no particular order.</returns>
    /// <exception cref="HttpRequestException">
    /// The relay couldn't be reached, closed the connection, sent what isn't Nostr, or didn't answer in time.
    /// </exception>
    public async Task<IReadOnlyList<NostrEvent>> ReadAsync(Uri relay, IReadOnlyList<NostrFilter> filters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(filters);

        using HttpMessageInvoker invoker = new(_handlers.CreateHandler(ClientName), disposeHandler: false);
        using ClientWebSocket socket = new();
        socket.Options.SetRequestHeader("User-Agent", _userAgent);
        await TimedAsync(relay, token => socket.ConnectAsync(relay, invoker, token), cancellationToken).ConfigureAwait(false);

        Dictionary<string, NostrEvent> found = new(StringComparer.Ordinal);
        int subscription = 0;
        int forged = 0;
        foreach (NostrFilter filter in filters)
        {
            NostrFilter page = filter;
            for (int pages = 0; pages < MaxPages; pages++)
            {
                string id = (++subscription).ToString(CultureInfo.InvariantCulture);
                (IReadOnlyList<NostrEvent> events, int notGenuine) = await RequestAsync(socket, relay, id, page, cancellationToken).ConfigureAwait(false);
                forged += notGenuine;
                int added = events.Count(nostrEvent => found.TryAdd(nostrEvent.Id, nostrEvent));
                if (page.Limit is null || added == 0)
                {
                    break;
                }

                page = page with { Until = events.Min(nostrEvent => nostrEvent.CreatedAt) };
            }
        }

        string name = Name(relay);
        if (forged > 0)
        {
            LogNotGenuine(_logger, forged, name);
        }

        await CloseAsync(socket).ConfigureAwait(false);
        LogRead(_logger, found.Count, subscription, name);
        return [.. found.Values];
    }

    /// <summary>
    /// One REQ, and the relay's events for it until it has sent all it holds; then the subscription is closed.
    /// </summary>
    /// <returns>The genuine events, none when the relay refused the filter, and how many weren't genuine.</returns>
    private async Task<(IReadOnlyList<NostrEvent> Events, int NotGenuine)> RequestAsync(
        ClientWebSocket socket, Uri relay, string id, NostrFilter filter, CancellationToken cancellationToken)
    {
        await TimedAsync(relay, token => SendAsync(socket, Request(id, filter), token), cancellationToken).ConfigureAwait(false);

        List<NostrEvent> events = [];
        int notGenuine = 0;
        while (true)
        {
            byte[] message = await TimedAsync(relay, token => ReceiveAsync(socket, relay, token), cancellationToken).ConfigureAwait(false);
            using JsonDocument document = Parse(message, relay);
            JsonElement root = document.RootElement;
            string type = root[0].GetString()!;
            if (type == "NOTICE")
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    string notice = root.GetRawText();
                    LogNotice(_logger, relay, notice);
                }

                continue;
            }

            if (root.GetArrayLength() < 2 || root[1].ValueKind != JsonValueKind.String || root[1].GetString() != id)
            {
                // Another subscription's, or a message ModShelf doesn't ask for, such as AUTH.
                continue;
            }

            switch (type)
            {
                case "EVENT" when root.GetArrayLength() > 2:
                    if (NostrEvent.Read(root[2]) is { } nostrEvent && NostrSignatures.IsGenuine(nostrEvent))
                    {
                        events.Add(nostrEvent);
                    }
                    else
                    {
                        notGenuine++;
                    }

                    break;
                case "EOSE":
                    await TimedAsync(relay, token => SendAsync(socket, $"[\"CLOSE\",\"{id}\"]", token), cancellationToken).ConfigureAwait(false);
                    return (events, notGenuine);
                case "CLOSED":
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        string answer = root.GetRawText();
                        LogRefused(_logger, relay, answer);
                    }

                    return ([], notGenuine);
            }
        }
    }

    /// <summary>
    /// <c>["REQ", id, filter]</c>.
    /// </summary>
    private static string Request(string id, NostrFilter filter)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("REQ");
            writer.WriteStringValue(id);
            filter.Write(writer);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static JsonDocument Parse(byte[] message, Uri relay)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(message);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() == 0
                || document.RootElement[0].ValueKind != JsonValueKind.String)
            {
                document.Dispose();
                throw NotNostr(relay, null);
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw NotNostr(relay, exception);
        }
    }

    /// <summary>
    /// The relay as messages name it: its host, and its path when it has one.
    /// </summary>
    internal static string Name(Uri relay) => relay.AbsolutePath is "/" or "" ? relay.Host : relay.Host + relay.AbsolutePath.TrimEnd('/');

    private static HttpRequestException NotNostr(Uri relay, Exception? inner)
        => new(HttpRequestError.InvalidResponse, $"DEG Mods' relay {Name(relay)} sent a message that isn't Nostr's.", inner);

    private static Task SendAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
        => socket.SendAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

    /// <summary>
    /// The next whole message, however many frames it came in.
    /// </summary>
    private static async Task<byte[]> ReceiveAsync(ClientWebSocket socket, Uri relay, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using MemoryStream message = new();
            while (true)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new HttpRequestException($"DEG Mods' relay {Name(relay)} closed the connection.");
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                {
                    throw new HttpRequestException(HttpRequestError.InvalidResponse, $"DEG Mods' relay {Name(relay)} sent a message larger than ModShelf takes.");
                }

                if (result.EndOfMessage)
                {
                    return message.ToArray();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// What a step does, within the time a relay has to answer; one that takes longer, or fails on the way, is an
    /// <see cref="HttpRequestException"/>.
    /// </summary>
    private async Task TimedAsync(Uri relay, Func<CancellationToken, Task> step, CancellationToken cancellationToken)
        => await TimedAsync(
            relay,
            async token =>
            {
                await step(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<T> TimedAsync<T>(Uri relay, Func<CancellationToken, Task<T>> step, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(_answerTimeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await step(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                $"DEG Mods' relay {Name(relay)} didn't answer within {_answerTimeout.TotalSeconds:0} seconds.", new TimeoutException(exception.Message, exception));
        }
        catch (WebSocketException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"DEG Mods' relay {Name(relay)} couldn't be reached: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Says goodbye, without waiting long for the relay's: the events are read.
    /// </summary>
    private async Task CloseAsync(ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1), _timeProvider);
        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // The relay went first; nothing is lost.
        }
    }
}
