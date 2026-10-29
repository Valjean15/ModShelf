namespace ModShelf.Sources.Abstractions.Quota;

/// <summary>
/// One allowance of requests, such as so many a day.
/// </summary>
public sealed record QuotaAllowance
{
    /// <param name="period">How long it lasts before it starts over, such as a day.</param>
    /// <param name="limit">How many requests it allows in that time.</param>
    /// <param name="remaining">How many of those are left.</param>
    /// <param name="resetsAt">When it starts over.</param>
    public QuotaAllowance(TimeSpan period, int limit, int remaining, DateTimeOffset resetsAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(remaining);

        Period = period;
        Limit = limit;
        Remaining = remaining;
        ResetsAt = resetsAt;
    }

    public TimeSpan Period { get; }

    public int Limit { get; }

    public int Remaining { get; }

    public DateTimeOffset ResetsAt { get; }
}
