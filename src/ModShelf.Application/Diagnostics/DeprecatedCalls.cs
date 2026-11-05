using Microsoft.Extensions.Logging;

using ModShelf.Application.Diagnostics.Ports;

namespace ModShelf.Application.Diagnostics;

/// <summary>
/// Counts deprecated calls in the <see cref="IDeprecatedCallStore"/>, at the time <see cref="TimeProvider"/> gives.
/// </summary>
internal sealed partial class DeprecatedCalls(IDeprecatedCallStore store, TimeProvider timeProvider, ILogger<DeprecatedCalls> logger) : IDeprecatedCalls
{
    /// <summary>
    /// The most of a client's name kept, which a line of the report holds.
    /// </summary>
    internal const int LongestClient = 64;

    public async Task CountAsync(string name, string code, string? client, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            await store.AddAsync(name.Trim(), code.Trim(), OneLine(client, LongestClient), timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogNotCounted(logger, exception, code.Trim());
        }
    }

    public Task<IReadOnlyList<DeprecatedCall>> ListAsync(CancellationToken cancellationToken) => store.LoadAsync(cancellationToken);

    /// <summary>
    /// The first line of <paramref name="text"/>, its other control characters as spaces, trimmed, and at most
    /// <paramref name="longest"/> characters; null when nothing is left.
    /// </summary>
    internal static string? OneLine(string? text, int longest)
    {
        if (text is null)
        {
            return null;
        }

        int end = text.AsSpan().IndexOfAny('\r', '\n');
        string line = new string([.. (end < 0 ? text : text[..end]).Select(character => char.IsControl(character) ? ' ' : character)]).Trim();
        if (line.Length > longest)
        {
            // Never half a character made of two.
            line = line[..(char.IsHighSurrogate(line[longest - 1]) ? longest - 1 : longest)].TrimEnd();
        }

        return line.Length == 0 ? null : line;
    }
}
