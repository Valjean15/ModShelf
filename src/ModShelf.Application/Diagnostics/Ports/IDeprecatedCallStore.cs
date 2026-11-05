namespace ModShelf.Application.Diagnostics.Ports;

/// <summary>
/// Keeps the count of <see cref="DeprecatedCall"/>s between runs, for every process that counts them, one at a time.
/// Implemented by Infrastructure.
/// </summary>
public interface IDeprecatedCallStore
{
    /// <summary>
    /// Where the count is kept, such as a file's path.
    /// </summary>
    string Location { get; }

    /// <returns>
    /// The calls counted, by name; none when nothing is counted yet, or what's kept can't be read, which is started again
    /// when a call is next counted.
    /// </returns>
    Task<IReadOnlyList<DeprecatedCall>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Counts one more call of <paramref name="name"/>, made at <paramref name="at"/> by <paramref name="client"/>: its
    /// count goes up by one, its first call stays, and its last call and client become these. Counting reads and writes
    /// the count in one step that no other counting, in this process or another, comes between.
    /// </summary>
    /// <exception cref="IOException">The call can't be counted; the count stays as it was.</exception>
    Task AddAsync(string name, string code, string? client, DateTimeOffset at, CancellationToken cancellationToken);
}
