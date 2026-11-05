using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Says whether two sites are in use as the test sets it, counting how often it's asked.
/// </summary>
internal sealed class ScriptedSitesInUse : ISitesInUse
{
    private int _asked;

    /// <summary>
    /// Whether two sites or more are in use.
    /// </summary>
    public bool Several { get; set; }

    public int Asked => Volatile.Read(ref _asked);

    public Task<bool> AreSeveralAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);
        return Task.FromResult(Several);
    }
}
