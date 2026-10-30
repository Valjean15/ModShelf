namespace ModShelf.Application.Diagnostics;

/// <summary>
/// A report of this ModShelf and its state, for a bug report.
/// </summary>
public interface IDiagnostics
{
    /// <summary>
    /// The report, a line per part, in English like the logs, for whoever reads the bug report. It holds no keys,
    /// account names, links or file names, and shows the user's home folder as <c>~</c>. A part that can't be read says
    /// so, and the rest is still written.
    /// </summary>
    Task<string> DescribeAsync(CancellationToken cancellationToken);
}
