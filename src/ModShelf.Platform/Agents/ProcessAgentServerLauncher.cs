using System.Diagnostics;
using System.Globalization;

namespace ModShelf.Platform.Agents;

/// <summary>
/// Starts the command line as a process of its own, without a window, reading what it says.
/// </summary>
internal sealed class ProcessAgentServerLauncher : IAgentServerLauncher
{
    /// <summary>
    /// What the command line says once it serves the address, such as "ModShelf's MCP server is at http://…".
    /// </summary>
    internal const string ServingSays = "MCP server is at http";

    public IRunningAgentServer Start(string executable, int port, int parent, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(environment);

        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
        };
        foreach (string argument in new[] { "mcp", "--http", "--port", port.ToString(CultureInfo.InvariantCulture), "--parent", parent.ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        return new Running(Process.Start(start) ?? throw new InvalidOperationException($"{executable} didn't start."));
    }

    private sealed class Running : IRunningAgentServer
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Running(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, line) =>
            {
                if (line.Data?.Contains(ServingSays, StringComparison.Ordinal) == true)
                {
                    _ready.TrySetResult(true);
                }
            };

            // What it says on standard error is in its log file too; read so that it never waits on a full pipe.
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Exited = EndAsync();
        }

        public Task<bool> Ready => _ready.Task;

        public Task<int> Exited { get; }

        public void Stop()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // It ended meanwhile.
            }
        }

        public void Dispose()
        {
            Stop();
            _process.Dispose();
        }

        private async Task<int> EndAsync()
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            _ready.TrySetResult(false);
            return _process.ExitCode;
        }
    }
}
