namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// Where the MCP server's address stands.
/// </summary>
/// <param name="Status">Whether it's served, and why not.</param>
/// <param name="Port">The port it's served at, or would be.</param>
/// <param name="ExitCode">For <see cref="AgentServerStatus.Failed"/>, the command line's exit code, when it ended; otherwise null.</param>
public sealed record AgentServerState(AgentServerStatus Status, int Port, int? ExitCode = null)
{
    /// <summary>
    /// Not served, agents being off.
    /// </summary>
    public static AgentServerState Off { get; } = new(AgentServerStatus.Off, 0);

    /// <summary>
    /// The address, served or not.
    /// </summary>
    public Uri Address => AgentAddress.Of(Port);
}
