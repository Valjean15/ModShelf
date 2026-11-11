namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Keeps the source's requests under mod.io's limit: at most so many in any minute, each waiting its turn; and none at
/// all while mod.io has asked ModShelf to slow down. mod.io says how many are left only once they're used up, with a
/// 429, and warns that asking on after one can get the credentials revoked.
/// </summary>
internal sealed class ModIoPacer(TimeProvider time, int perMinute) : IDisposable
{
    /// <summary>
    /// The time the limit counts requests in, and how long mod.io is left alone after a 429 that doesn't say.
    /// </summary>
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly Lock _holding = new();
    private DateTimeOffset _heldUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// Until when nothing is sent, after mod.io asked ModShelf to slow down; null when requests may go.
    /// </summary>
    public DateTimeOffset? HeldUntil
    {
        get
        {
            lock (_holding)
            {
                return time.GetUtcNow() < _heldUntil ? _heldUntil : null;
            }
        }
    }

    /// <summary>
    /// Sends nothing for <paramref name="wait"/> from now, or for a minute when it's not more than nothing; a hold that
    /// ends later already stays.
    /// </summary>
    /// <returns>Until when nothing is sent.</returns>
    public DateTimeOffset Hold(TimeSpan wait)
    {
        lock (_holding)
        {
            DateTimeOffset until = time.GetUtcNow() + (wait > TimeSpan.Zero ? wait : Window);
            if (until > _heldUntil)
            {
                _heldUntil = until;
            }

            return _heldUntil;
        }
    }

    /// <summary>
    /// Sends nothing until <paramref name="until"/>, by this clock, or for a minute when that's not later than now; a
    /// hold that ends later already stays.
    /// </summary>
    /// <returns>Until when nothing is sent.</returns>
    public DateTimeOffset Hold(DateTimeOffset until) => Hold(until - time.GetUtcNow());

    /// <summary>
    /// Waits until a request may be sent, and counts it.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                DateTimeOffset now = time.GetUtcNow();
                while (_sent.Count > 0 && now - _sent.Peek() >= Window)
                {
                    _sent.Dequeue();
                }

                if (_sent.Count < perMinute)
                {
                    _sent.Enqueue(now);
                    return;
                }

                await Task.Delay(_sent.Peek() + Window - now, time, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Dispose() => _turn.Dispose();
}
