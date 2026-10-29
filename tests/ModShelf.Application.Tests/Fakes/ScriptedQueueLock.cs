using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A download queue lock that another process holds while the test says so.
/// </summary>
internal sealed class ScriptedQueueLock : IDownloadQueueLock
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Whether another process holds the lock, so this one can't take it.
    /// </summary>
    public bool HeldElsewhere { get; set; }

    /// <summary>
    /// Whether this process holds it.
    /// </summary>
    public bool Held { get; private set; }

    /// <summary>
    /// How many times this process tried to take it.
    /// </summary>
    public int Tries { get; private set; }

    public bool TryAcquire()
    {
        lock (_gate)
        {
            Tries++;
            Held = Held || !HeldElsewhere;
            return Held;
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            Held = false;
        }
    }
}
