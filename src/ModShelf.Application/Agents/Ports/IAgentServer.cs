namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// ModShelf's MCP server, as the desktop app offers it to agents: the command their apps start it with, and its address,
/// which the desktop app serves while agents may use ModShelf, by running the command line's <c>mcp --http</c> until it
/// quits (ADR 0080).
/// </summary>
public interface IAgentServer
{
    /// <summary>
    /// The command an agent's app starts the server with; null when the command line isn't beside the app, or not at a
    /// path that lasts, as in an AppImage, which the system mounts somewhere new each time it starts.
    /// </summary>
    AgentCommand? Command { get; }

    /// <summary>
    /// Where the address stands.
    /// </summary>
    AgentServerState State { get; }

    /// <summary>
    /// Raised, on any thread, when <see cref="State"/> changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Serves the address at <paramref name="port"/> when <paramref name="enabled"/>, starting or moving it as needed, or
    /// stops serving it. It returns once the address is served, or why not is known.
    /// </summary>
    Task ApplyAsync(bool enabled, int port, CancellationToken cancellationToken);
}
