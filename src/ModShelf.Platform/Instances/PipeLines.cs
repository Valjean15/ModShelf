using System.Text;

namespace ModShelf.Platform.Instances;

/// <summary>
/// Reading the lines ModShelf's pipes carry, one message a line.
/// </summary>
internal static class PipeLines
{
    /// <summary>
    /// The next line, without its line ending, of at most <paramref name="maxLength"/> characters; null when the other
    /// end hung up first.
    /// </summary>
    /// <exception cref="InvalidDataException">The line is longer.</exception>
    public static async Task<string?> ReadLineAsync(StreamReader reader, int maxLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        StringBuilder line = new();
        char[] next = new char[1];
        while (await reader.ReadAsync(next, cancellationToken).ConfigureAwait(false) == 1)
        {
            if (next[0] == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (line.Length == maxLength)
            {
                throw new InvalidDataException($"A message on the pipe is longer than {maxLength} characters.");
            }

            line.Append(next[0]);
        }

        return line.Length == 0 ? null : line.ToString();
    }
}
