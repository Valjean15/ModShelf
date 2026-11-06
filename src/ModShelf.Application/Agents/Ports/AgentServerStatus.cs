namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// Where the MCP server's address stands, as the desktop app serves it.
/// </summary>
public enum AgentServerStatus
{
    /// <summary>Not served: agents may not use ModShelf.</summary>
    Off,

    /// <summary>Starting.</summary>
    Starting,

    /// <summary>Served, at <see cref="AgentServerState.Address"/>.</summary>
    Running,

    /// <summary>Not served: another app has the port.</summary>
    PortInUse,

    /// <summary>Not served: the command line, which serves it, isn't beside the app, as in some builds from source.</summary>
    NoCommand,

    /// <summary>Not served: it stopped, or couldn't start, for another reason; the log says which.</summary>
    Failed,
}
