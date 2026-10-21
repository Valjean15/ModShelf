namespace ModShelf.Sources.Abstractions.Downloads;

/// <summary>
/// The outcome of <see cref="IDownloadResolver.ResolveAsync"/>: either a <see cref="DownloadTicket"/>
/// or <see cref="HandoffRequired"/>.
/// </summary>
public abstract record ResolveResult
{
    // Only the results defined in this assembly exist, so callers can handle every case.
    private protected ResolveResult()
    {
    }
}
