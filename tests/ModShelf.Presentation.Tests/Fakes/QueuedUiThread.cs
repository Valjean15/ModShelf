using System.Collections.Concurrent;

using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A UI thread the test runs: actions wait until <see cref="RunPending"/>, or run at once when <see cref="Inline"/>.
/// Actions can be posted from any thread.
/// </summary>
internal sealed class QueuedUiThread : IUiThread
{
    private readonly ConcurrentQueue<Action> _pending = new();

    /// <summary>
    /// Whether actions run as they're posted, as on the UI thread itself.
    /// </summary>
    public bool Inline { get; set; } = true;

    public int Pending => _pending.Count;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Inline)
        {
            action();
        }
        else
        {
            _pending.Enqueue(action);
        }
    }

    public void RunPending()
    {
        while (_pending.TryDequeue(out Action? action))
        {
            action();
        }
    }
}
