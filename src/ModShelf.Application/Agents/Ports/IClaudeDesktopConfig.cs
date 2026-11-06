namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// Claude Desktop's configuration file, <c>claude_desktop_config.json</c>, whose <c>mcpServers</c> list the servers it
/// starts: where it is on this computer, and ModShelf's server in it (ADR 0080).
/// </summary>
public interface IClaudeDesktopConfig
{
    /// <summary>
    /// Where the file is, or would be; null when Claude Desktop isn't found.
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// Where adding ModShelf's server keeps a copy of the file as it was; null when Claude Desktop isn't found.
    /// </summary>
    string? BackupPath { get; }

    /// <summary>
    /// Whether the file has <paramref name="command"/> as ModShelf's server.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't JSON Claude Desktop could read.</exception>
    /// <exception cref="IOException">The file can't be read.</exception>
    Task<ClaudeDesktopServer> ReadAsync(AgentCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Adds <paramref name="command"/> as the server named <c>modshelf</c>, in place of one by that name, keeping the
    /// file's other servers and settings, and a copy of the file as it was.
    /// </summary>
    /// <exception cref="InvalidOperationException">Claude Desktop isn't found.</exception>
    /// <exception cref="InvalidDataException">The file isn't JSON Claude Desktop could read; it's left as it is.</exception>
    /// <exception cref="IOException">The file can't be written.</exception>
    Task AddAsync(AgentCommand command, CancellationToken cancellationToken);
}
