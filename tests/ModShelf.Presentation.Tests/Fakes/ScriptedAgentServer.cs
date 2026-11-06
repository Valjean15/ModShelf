using ModShelf.Application.Agents.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The MCP server's command and address as the test sets them: serving the address at a port gives
/// <see cref="Outcome"/>'s state, running by default, and each time it's asked is recorded.
/// </summary>
internal sealed class ScriptedAgentServer : IAgentServer
{
    /// <summary>
    /// The installed command line on Windows.
    /// </summary>
    public static AgentCommand Installed { get; } = new(
        @"C:\Users\kestrel\AppData\Local\ModShelf.App\current\ModShelf.Cli.exe",
        ["mcp"],
        new Dictionary<string, string>());

    public event EventHandler? StateChanged;

    public AgentCommand? Command { get; set; } = Installed;

    public AgentServerState State { get; private set; } = AgentServerState.Off;

    /// <summary>
    /// What serving the address at a port gives; null to run.
    /// </summary>
    public Func<int, AgentServerState>? Outcome { get; set; }

    /// <summary>
    /// Each time the address was asked to be served, or not, and at what port.
    /// </summary>
    public List<(bool Enabled, int Port)> Applied { get; } = [];

    public Task ApplyAsync(bool enabled, int port, CancellationToken cancellationToken)
    {
        Applied.Add((enabled, port));
        Show(enabled ? Outcome?.Invoke(port) ?? new AgentServerState(AgentServerStatus.Running, port) : AgentServerState.Off);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Changes the state, as the command line would.
    /// </summary>
    public void Show(AgentServerState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
