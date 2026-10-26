using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A source whose answers the test scripts in advance. Once the script runs out, it hands out tickets.
/// It records the file and handoff link of every call.
/// </summary>
internal sealed class ScriptedResolver(SourceId id) : IDownloadResolver
{
    /// <summary>
    /// The secret part of every ticket URL, so tests can check that it never reaches the log.
    /// </summary>
    public const string UrlSecret = "signature-0f9e8d7c";

    private readonly ConcurrentQueue<Func<Task<ResolveResult>>> _script = new();
    private readonly ConcurrentQueue<ResolveCall> _calls = new();

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public int Calls => _calls.Count;

    /// <summary>
    /// Every call so far, in order.
    /// </summary>
    public IReadOnlyList<ResolveCall> Resolved => [.. _calls];

    public static DownloadTicket Ticket()
        => new(new Uri($"https://cdn.example.com/files/SkyUI_5_2_SE.7z?{UrlSecret}"));

    public ScriptedResolver ThenReturn(ResolveResult result)
    {
        _script.Enqueue(() => Task.FromResult(result));
        return this;
    }

    public ScriptedResolver ThenThrow(Exception exception)
    {
        _script.Enqueue(() => Task.FromException<ResolveResult>(exception));
        return this;
    }

    /// <summary>
    /// Answers the next call with <paramref name="result"/> only once <paramref name="gate"/> completes.
    /// </summary>
    public ScriptedResolver ThenReturnWhen(Task gate, ResolveResult result)
    {
        _script.Enqueue(async () =>
        {
            await gate.ConfigureAwait(false);
            return result;
        });
        return this;
    }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        _calls.Enqueue(new ResolveCall(file, handoff));
        return _script.TryDequeue(out Func<Task<ResolveResult>>? scripted)
            ? scripted()
            : Task.FromResult<ResolveResult>(Ticket());
    }
}
