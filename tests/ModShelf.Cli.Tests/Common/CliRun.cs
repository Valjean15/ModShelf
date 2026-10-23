using System.CommandLine;

using Microsoft.Extensions.Hosting;

using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// One command running in this process, as <c>Program.cs</c> runs it, with the test as the terminal: it reads the
/// output as it's written, types the input, and can press Ctrl+C.
/// </summary>
internal sealed class CliRun : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly RecordingTextWriter _output = new();
    private readonly RecordingTextWriter _error = new();
    private readonly CancellationTokenSource _ctrlC = new();
    private readonly Task<int> _exitCode;

    public CliRun(string[] arguments, TextReader input, Func<bool, IHost> buildHost)
    {
        CliEnvironment environment = new(input, _output, _error, InputIsTerminal: false, OutputIsTerminal: false, buildHost);
        ParseResult parsed = CliCommands.Create(environment).Parse(arguments);
        InvocationConfiguration invocation = new()
        {
            Output = _output,
            Error = _error,

            // The test presses Ctrl+C through the token; the test process's own signals are left alone.
            ProcessTerminationTimeout = null,

            // An unexpected exception fails the test with its stack trace, rather than becoming exit code 1.
            EnableDefaultExceptionHandler = false,
        };
        _exitCode = Task.Run(() => parsed.InvokeAsync(invocation, _ctrlC.Token));
    }

    public string Output => _output.Text;

    public Task WaitForOutputAsync(string expected, CancellationToken cancellationToken)
        => _output.WaitForAsync(expected, Timeout, cancellationToken);

    /// <summary>
    /// What Ctrl+C does in a terminal: cancels the command's token.
    /// </summary>
    public Task PressCtrlCAsync() => _ctrlC.CancelAsync();

    public async Task<CliResult> WaitForExitAsync(CancellationToken cancellationToken)
    {
        int exitCode = await _exitCode.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
        return new CliResult(exitCode, _output.Text, _error.Text);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_exitCode.IsCompleted)
        {
            await _ctrlC.CancelAsync().ConfigureAwait(false);
            try
            {
                await _exitCode.WaitAsync(Timeout).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                // The test failed already; this only stops what it left running.
            }
        }

        _ctrlC.Dispose();
        await _output.DisposeAsync().ConfigureAwait(false);
        await _error.DisposeAsync().ConfigureAwait(false);
    }
}
