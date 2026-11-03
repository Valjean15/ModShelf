using ModShelf.Application.Library;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The library's history as the test says, recording what it was asked for.
/// </summary>
internal sealed class ScriptedLibraryHistory : ILibraryHistory
{
    /// <summary>
    /// What happened, the newest first.
    /// </summary>
    public List<LibraryEvent> Events { get; } = [];

    /// <summary>
    /// The limits asked for, in order.
    /// </summary>
    public List<int> Asked { get; } = [];

    /// <summary>
    /// What the next listing throws instead; null for none.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// While set, listings wait for it, as a slow database does.
    /// </summary>
    public TaskCompletionSource? Held { get; set; }

    public async Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(limit);
        if (Held is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return Refusal is { } refusal ? throw refusal : [.. Events.Where(happened => since is null || happened.At >= since).Take(limit)];
    }
}
