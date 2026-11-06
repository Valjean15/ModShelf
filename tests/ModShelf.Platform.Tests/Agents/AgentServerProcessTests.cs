using ModShelf.Application.Agents.Ports;
using ModShelf.Platform.Agents;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Agents;

/// <summary>
/// The address, as the desktop app serves it: the command line started while agents may use ModShelf, at the port,
/// for this process, with this library's environment; moved, stopped, and what it says when it can't serve it.
/// </summary>
public sealed class AgentServerProcessTests : IDisposable
{
    private const string Executable = "/opt/ModShelf/ModShelf.Cli";

    private static readonly Dictionary<string, string> Environment = new() { ["ModShelf__DatabasePath"] = "/home/kestrel/library.db" };

    private readonly ScriptedLauncher _launcher = new();
    private readonly RecordingLogger<AgentServerProcess> _logger = new();
    private readonly List<AgentServerStatus> _seen = [];
    private readonly AgentServerProcess _server;

    public AgentServerProcessTests()
    {
        _server = new AgentServerProcess(Executable, lasting: true, Environment, _launcher, _logger, TimeSpan.FromSeconds(10));
        _server.StateChanged += (_, _) => _seen.Add(_server.State.Status);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task OffNothingRuns()
    {
        await _server.ApplyAsync(false, 47823, Cancellation);

        Assert.Empty(_launcher.Started);
        Assert.Equal(AgentServerState.Off, _server.State);
        Assert.Empty(_seen);
        AgentCommand command = _server.Command!;
        Assert.Equal((Executable, "mcp"), (command.Path, Assert.Single(command.Arguments)));
        Assert.Same(Environment, command.Environment);
    }

    [Fact]
    public async Task OnItRunsTheCommandLineAtThePortForThisProcess()
    {
        await _server.ApplyAsync(true, 47823, Cancellation);

        (string executable, int port, int parent, IReadOnlyDictionary<string, string> environment, ScriptedLauncher.Running running) = Assert.Single(_launcher.Started);
        Assert.Equal((Executable, 47823, System.Environment.ProcessId), (executable, port, parent));
        Assert.Same(Environment, environment);
        Assert.Equal(new AgentServerState(AgentServerStatus.Running, 47823), _server.State);
        Assert.Equal(new Uri("http://127.0.0.1:47823/mcp"), _server.State.Address);
        Assert.Equal([AgentServerStatus.Starting, AgentServerStatus.Running], _seen);
        Assert.False(running.Stopped);

        // Again at the same port, it goes on as it is.
        await _server.ApplyAsync(true, 47823, Cancellation);
        Assert.Single(_launcher.Started);
    }

    [Fact]
    public async Task AnotherPortMovesItAndOffStopsIt()
    {
        await _server.ApplyAsync(true, 47823, Cancellation);

        await _server.ApplyAsync(true, 50000, Cancellation);

        Assert.Equal([47823, 50000], _launcher.Started.Select(started => started.Port));
        Assert.True(_launcher.Started[0].Running.Stopped);
        Assert.Equal(new AgentServerState(AgentServerStatus.Running, 50000), _server.State);

        await _server.ApplyAsync(false, 50000, Cancellation);

        Assert.True(_launcher.Started[1].Running.Stopped);
        Assert.Equal(AgentServerState.Off, _server.State);
    }

    [Fact]
    public async Task APortInUseIsSaid()
    {
        _launcher.EndsAtOnce = 4;

        await _server.ApplyAsync(true, 47823, Cancellation);

        Assert.Equal(new AgentServerState(AgentServerStatus.PortInUse, 47823), _server.State);
        Assert.Contains(_logger.Entries, entry => entry.Contains("ended, with exit code 4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherEndBeforeServingIsAFailureWithItsCode()
    {
        _launcher.EndsAtOnce = 70;

        await _server.ApplyAsync(true, 47823, Cancellation);

        Assert.Equal(new AgentServerState(AgentServerStatus.Failed, 47823, 70), _server.State);
    }

    [Fact]
    public async Task OneThatNeverSaysItServesIsStoppedInTime()
    {
        using AgentServerProcess slow = new(Executable, lasting: true, Environment, _launcher, _logger, TimeSpan.FromMilliseconds(50));
        _launcher.NeverReady = true;

        await slow.ApplyAsync(true, 47823, Cancellation);

        Assert.Equal(new AgentServerState(AgentServerStatus.Failed, 47823), slow.State);
        Assert.True(Assert.Single(_launcher.Started).Running.Stopped);
        Assert.Contains(_logger.Entries, entry => entry.Contains("didn't start in time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEndWhileServingIsSaid()
    {
        await _server.ApplyAsync(true, 47823, Cancellation);
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.StateChanged += (_, _) => changed.TrySetResult();

        _launcher.Started[0].Running.End(1);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal(new AgentServerState(AgentServerStatus.Failed, 47823, 1), _server.State);

        // Turned on again, it starts anew.
        await _server.ApplyAsync(true, 47823, Cancellation);
        Assert.Equal(2, _launcher.Started.Count);
        Assert.Equal(AgentServerStatus.Running, _server.State.Status);
    }

    [Fact]
    public async Task WithoutTheCommandLineItCantServeIt()
    {
        using AgentServerProcess none = new(null, lasting: true, Environment, _launcher, _logger);

        await none.ApplyAsync(true, 47823, Cancellation);

        Assert.Equal(new AgentServerState(AgentServerStatus.NoCommand, 47823), none.State);
        Assert.Null(none.Command);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task AnAppImagesCommandLineServesTheAddressButIsNoCommandForAgentsApps()
    {
        using AgentServerProcess mounted = new("/tmp/.mount_ModShelf/usr/bin/ModShelf.Cli", lasting: false, Environment, _launcher, _logger);

        await mounted.ApplyAsync(true, 47823, Cancellation);

        Assert.Null(mounted.Command);
        Assert.Equal(AgentServerStatus.Running, mounted.State.Status);
    }

    [Fact]
    public async Task DisposedItStops()
    {
        await _server.ApplyAsync(true, 47823, Cancellation);

        _server.Dispose();

        Assert.True(_launcher.Started[0].Running.Stopped);
    }

    /// <summary>
    /// Starts nothing: each start is recorded, and says it serves the address at once, unless the test says otherwise.
    /// </summary>
    private sealed class ScriptedLauncher : IAgentServerLauncher
    {
        public List<(string Executable, int Port, int Parent, IReadOnlyDictionary<string, string> Environment, Running Running)> Started { get; } = [];

        /// <summary>
        /// The exit code each start ends with before it serves the address; null to serve it.
        /// </summary>
        public int? EndsAtOnce { get; set; }

        public bool NeverReady { get; set; }

        public IRunningAgentServer Start(string executable, int port, int parent, IReadOnlyDictionary<string, string> environment)
        {
            Running running = new();
            if (EndsAtOnce is { } code)
            {
                running.End(code);
            }
            else if (!NeverReady)
            {
                running.Serve();
            }

            Started.Add((executable, port, parent, environment, running));
            return running;
        }

        public sealed class Running : IRunningAgentServer
        {
            private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<bool> Ready => _ready.Task;

            public Task<int> Exited => _exited.Task;

            public bool Stopped { get; private set; }

            public void Serve() => _ready.TrySetResult(true);

            public void End(int code)
            {
                _ready.TrySetResult(false);
                _exited.TrySetResult(code);
            }

            public void Stop()
            {
                Stopped = true;
                End(-1);
            }

            public void Dispose() => Stop();
        }
    }
}
