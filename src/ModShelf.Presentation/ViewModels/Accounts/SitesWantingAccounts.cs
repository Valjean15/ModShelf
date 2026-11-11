using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// The sites whose downloads failed for want of an account, as the Downloads screen lists them: their rows in Settings ›
/// Accounts say so, and Settings' badge counts them (ADR 0094).
/// </summary>
public sealed class SitesWantingAccounts
{
    private readonly Lock _gate = new();
    private IReadOnlySet<SourceId> _sites = new HashSet<SourceId>();

    /// <summary>
    /// Raised when the sites change.
    /// </summary>
    public event EventHandler? Changed;

    public IReadOnlySet<SourceId> Sites
    {
        get
        {
            lock (_gate)
            {
                return _sites;
            }
        }
    }

    /// <summary>
    /// Says which sites' downloads wait for an account now.
    /// </summary>
    internal void Show(IEnumerable<SourceId> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);

        HashSet<SourceId> now = [.. sites];
        lock (_gate)
        {
            if (now.SetEquals(_sites))
            {
                return;
            }

            _sites = now;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
