using System.Collections.Concurrent;
using System.Text;

namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// An HTTP server stand-in: answers each request with the next scripted response, and records what it was asked.
/// </summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _script = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private Func<HttpResponseMessage>? _fallback;

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    public ScriptedHttpHandler Respond(Func<HttpResponseMessage> response)
    {
        _script.Enqueue(response);
        return this;
    }

    public ScriptedHttpHandler Throw(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    /// <summary>
    /// The response for every request once the script has run out.
    /// </summary>
    public ScriptedHttpHandler RespondAlways(Func<HttpResponseMessage> response)
    {
        _fallback = response;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new RecordedRequest(
            request.Headers.Range?.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString(),
            request.RequestUri,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase),
            await ReadBodyAsync(request.Content, cancellationToken)));

        Func<HttpResponseMessage> next = _script.TryDequeue(out Func<HttpResponseMessage>? scripted)
            ? scripted
            : _fallback ?? throw new InvalidOperationException("The test didn't expect another request.");
        return next();
    }

    /// <summary>
    /// Reads the body the way a connection sends it. Unlike <see cref="HttpContent.ReadAsStringAsync()"/>, this doesn't
    /// buffer the content, so a body that can be read only once fails the second time, as it would on the network.
    /// </summary>
    private static async Task<string?> ReadBodyAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        using MemoryStream body = new();
        await content.CopyToAsync(body, cancellationToken);
        return Encoding.UTF8.GetString(body.ToArray());
    }
}
