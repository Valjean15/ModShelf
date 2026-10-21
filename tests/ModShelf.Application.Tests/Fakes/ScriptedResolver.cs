using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A source whose answers the test scripts in advance. Once the script runs out, it hands out tickets.
/// </summary>
internal sealed class ScriptedResolver(SourceId id) : IDownloadResolver
{
    /// <summary>
    /// The secret part of every ticket URL, so tests can check that it never reaches the log.
    /// </summary>
    public const string UrlSecret = "signature-0f9e8d7c";

    private readonly ConcurrentQueue<Func<ResolveResult>> _script = new();
    private int _calls;

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public int Calls => Volatile.Read(ref _calls);

    public static DownloadTicket Ticket()
        => new(new Uri($"https://cdn.example.com/files/SkyUI_5_2_SE.7z?{UrlSecret}"));

    public ScriptedResolver ThenReturn(ResolveResult result)
    {
        _script.Enqueue(() => result);
        return this;
    }

    public ScriptedResolver ThenThrow(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        Func<ResolveResult> next = _script.TryDequeue(out Func<ResolveResult>? scripted) ? scripted : Ticket;
        return Task.FromResult(next());
    }
}
