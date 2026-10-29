namespace ModShelf.Sources.Abstractions.Quota;

/// <summary>
/// What's left of an account's allowances of requests, as its site said.
/// </summary>
public sealed record ApiQuota
{
    /// <param name="allowances">At least one, the longest first, such as a day's and then an hour's.</param>
    public ApiQuota(IReadOnlyList<QuotaAllowance> allowances)
    {
        ArgumentNullException.ThrowIfNull(allowances);
        if (allowances.Count == 0 || allowances.Any(allowance => allowance is null))
        {
            throw new ArgumentException("A quota has at least one allowance, and no empty one.", nameof(allowances));
        }

        Allowances = allowances;
    }

    /// <summary>
    /// The allowances, the longest first.
    /// </summary>
    public IReadOnlyList<QuotaAllowance> Allowances { get; }
}
