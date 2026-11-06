using ModShelf.Application.Agents.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Claude Desktop's configuration as the test sets it: whether it has ModShelf's server, what reading or adding it
/// throws, and each server added, after which it has it.
/// </summary>
internal sealed class ScriptedClaudeDesktop : IClaudeDesktopConfig
{
    public string? FilePath { get; set; } = @"C:\Users\kestrel\AppData\Roaming\Claude\claude_desktop_config.json";

    public string? BackupPath => FilePath is null ? null : Path.ChangeExtension(FilePath, ".modshelf-backup.json");

    public ClaudeDesktopServer Found { get; set; } = ClaudeDesktopServer.Absent;

    public Exception? ReadFails { get; set; }

    public Exception? AddFails { get; set; }

    public List<AgentCommand> Added { get; } = [];

    public Task<ClaudeDesktopServer> ReadAsync(AgentCommand command, CancellationToken cancellationToken)
        => ReadFails is { } failure ? Task.FromException<ClaudeDesktopServer>(failure) : Task.FromResult(Found);

    public Task AddAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        if (AddFails is { } failure)
        {
            return Task.FromException(failure);
        }

        Added.Add(command);
        Found = ClaudeDesktopServer.Same;
        return Task.CompletedTask;
    }
}
