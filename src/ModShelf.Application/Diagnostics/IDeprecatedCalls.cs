namespace ModShelf.Application.Diagnostics;

/// <summary>
/// Counts the calls that use a deprecated argument, such as the MCP server's <c>download</c> with <c>link</c>, between
/// runs and across the processes making them, for the diagnostics report.
/// </summary>
public interface IDeprecatedCalls
{
    /// <summary>
    /// Counts one more call that used the deprecated argument <paramref name="name"/>, made now. It never fails the call:
    /// a count that can't be kept is logged.
    /// </summary>
    /// <param name="name">Which, as its tool and argument, such as <c>download.link</c>.</param>
    /// <param name="code">Its code, such as <c>DL_LINK_ARG</c>.</param>
    /// <param name="client">What made the call, such as an MCP client's name, or null when it didn't say. Only its
    /// first line is kept, and not too much of it.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="code"/> is blank.</exception>
    Task CountAsync(string name, string code, string? client, CancellationToken cancellationToken);

    /// <returns>The deprecated arguments used, by name; none when no call is counted yet, or the count can't be read.</returns>
    Task<IReadOnlyList<DeprecatedCall>> ListAsync(CancellationToken cancellationToken);
}
