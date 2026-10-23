using System.Diagnostics;
using System.Text;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// Stands in for the terminal's output: keeps what a command writes, which a test can read while the command runs.
/// </summary>
internal sealed class RecordingTextWriter : TextWriter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly Lock _gate = new();
    private readonly StringBuilder _text = new();

    public override Encoding Encoding => Encoding.UTF8;

    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    public override void Write(char value)
    {
        lock (_gate)
        {
            _text.Append(value);
        }
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            _text.Append(value);
        }
    }

    /// <summary>
    /// Waits until the output holds <paramref name="expected"/>.
    /// </summary>
    /// <exception cref="TimeoutException">It didn't within <paramref name="timeout"/>.</exception>
    public async Task WaitForAsync(string expected, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!Text.Contains(expected, StringComparison.Ordinal))
        {
            if (waited.Elapsed > timeout)
            {
                throw new TimeoutException($"The output never showed \"{expected}\". It holds:{Environment.NewLine}{Text}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
