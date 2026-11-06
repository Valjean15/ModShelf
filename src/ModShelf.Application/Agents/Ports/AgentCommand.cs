namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// How an agent's app starts ModShelf's MCP server itself, on standard input and output: the command line's executable
/// with <c>mcp</c>, and the settings this library needs in its environment, such as <c>ModShelf__DatabasePath</c>.
/// </summary>
/// <param name="Path">The command line's executable, a full path.</param>
/// <param name="Arguments">Its arguments: <c>mcp</c>.</param>
/// <param name="Environment">
/// The environment variables it needs to use this library, by name; none for the default library. Never a secret: the
/// library's folders and the name its credentials are kept under.
/// </param>
public sealed record AgentCommand(string Path, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);
