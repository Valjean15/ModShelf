using System.Collections.Concurrent;

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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new RecordedRequest(
            request.Headers.Range?.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString()));

        Func<HttpResponseMessage> next = _script.TryDequeue(out Func<HttpResponseMessage>? scripted)
            ? scripted
            : _fallback ?? throw new InvalidOperationException("The test didn't expect another request.");
        return Task.FromResult(next());
    }
}
