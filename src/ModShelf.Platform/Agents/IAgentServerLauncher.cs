namespace ModShelf.Platform.Agents;

/// <summary>
/// Starts the command line's <c>mcp --http</c>, which serves the MCP server's address; tests start their own.
/// </summary>
internal interface IAgentServerLauncher
{
    /// <param name="executable">The command line's executable.</param>
    /// <param name="port">The address's port.</param>
    /// <param name="parent">The ID of the process whose end ends the server: this one's.</param>
    /// <param name="environment">What keeps the command line on this library.</param>
    IRunningAgentServer Start(string executable, int port, int parent, IReadOnlyDictionary<string, string> environment);
}
