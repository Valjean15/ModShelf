namespace ModShelf.Platform.Agents;

/// <summary>
/// Where the command line is, and what keeps it on this library, for the MCP server the desktop app offers agents.
/// </summary>
/// <param name="CommandLine">The command line's executable, a full path; null to look beside the app.</param>
/// <param name="Environment">
/// The environment variables that keep the command line on this library, by name, such as <c>ModShelf__DatabasePath</c>;
/// none for the default library.
/// </param>
public sealed record AgentServerOptions(string? CommandLine, IReadOnlyDictionary<string, string> Environment);
