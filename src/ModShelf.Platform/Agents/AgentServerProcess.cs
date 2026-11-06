using Microsoft.Extensions.Logging;

using ModShelf.Application.Agents.Ports;

namespace ModShelf.Platform.Agents;

/// <summary>
/// The MCP server the desktop app offers agents: the command their apps start it with, and its address, which the command
/// line's <c>mcp --http</c> serves while agents may use ModShelf, as a process of its own that ends with the app (ADR 0080).
/// </summary>
internal sealed partial class AgentServerProcess : IAgentServer, IDisposable
{
    /// <summary>
    /// How long the command line has to say it serves the address, by default.
    /// </summary>
    internal static readonly TimeSpan DefaultStartsWithin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The command line's exit code when another app has the port (its <c>ExitCodes.PortInUse</c>).
    /// </summary>
    internal const int PortInUseExitCode = 4;

    private readonly string? _executable;
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly IAgentServerLauncher _launcher;
    private readonly ILogger _logger;
    private readonly TimeSpan _startsWithin;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRunningAgentServer? _running;
    private AgentServerState _state = AgentServerState.Off;

    /// <param name="executable">The command line's executable; null when it isn't found.</param>
    /// <param name="lasting">Whether <paramref name="executable"/> stays where it is, so that an agent's app can start it.</param>
    /// <param name="startsWithin">How long the command line has to say it serves the address; null for 30 seconds.</param>
    public AgentServerProcess(
        string? executable,
        bool lasting,
        IReadOnlyDictionary<string, string> environment,
        IAgentServerLauncher launcher,
        ILogger logger,
        TimeSpan? startsWithin = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);

        _executable = executable;
        _environment = environment;
        _launcher = launcher;
        _logger = logger;
        _startsWithin = startsWithin ?? DefaultStartsWithin;
        Command = executable is not null && lasting ? new AgentCommand(executable, ["mcp"], environment) : null;
    }

    public event EventHandler? StateChanged;

    public AgentCommand? Command { get; }

    public AgentServerState State => Volatile.Read(ref _state);

    public async Task ApplyAsync(bool enabled, int port, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled)
            {
                StopRunning();
                Show(AgentServerState.Off);
                return;
            }

            if (_running is not null && State is { Status: AgentServerStatus.Running } running && running.Port == port)
            {
                return;
            }

            StopRunning();
            if (_executable is null)
            {
                LogNoCommandLine(_logger);
                Show(new AgentServerState(AgentServerStatus.NoCommand, port));
                return;
            }

            Show(new AgentServerState(AgentServerStatus.Starting, port));
            IRunningAgentServer started = _launcher.Start(_executable, port, Environment.ProcessId, _environment);
            _running = started;
            Task finished = await Task.WhenAny(started.Ready, Task.Delay(_startsWithin, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (finished == started.Ready && started.Ready.Result)
            {
                LogServing(_logger, port);
                Show(new AgentServerState(AgentServerStatus.Running, port));
                _ = WatchAsync(started, port);
                return;
            }

            StopRunning();
            if (finished != started.Ready)
            {
                LogSlow(_logger, port);
                Show(new AgentServerState(AgentServerStatus.Failed, port));
                return;
            }

            int code = await started.Exited.ConfigureAwait(false);
            LogEnded(_logger, port, code);
            Show(new AgentServerState(code == PortInUseExitCode ? AgentServerStatus.PortInUse : AgentServerStatus.Failed, port, code == PortInUseExitCode ? null : code));
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        StopRunning();
        _gate.Dispose();
    }

    /// <summary>
    /// Says when the command line ends of itself while it serves the address.
    /// </summary>
    private async Task WatchAsync(IRunningAgentServer running, int port)
    {
        int code = await running.Exited.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_running == running)
            {
                LogEnded(_logger, port, code);
                _running = null;
                running.Dispose();
                Show(new AgentServerState(AgentServerStatus.Failed, port, code));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopRunning()
    {
        IRunningAgentServer? running = _running;
        _running = null;
        running?.Dispose();
    }

    private void Show(AgentServerState state)
    {
        if (Interlocked.Exchange(ref _state, state) != state)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
