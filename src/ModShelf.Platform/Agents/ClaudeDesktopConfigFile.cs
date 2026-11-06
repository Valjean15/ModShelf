using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Application.Agents;
using ModShelf.Application.Agents.Ports;

namespace ModShelf.Platform.Agents;

/// <summary>
/// Claude Desktop's <c>claude_desktop_config.json</c>: ModShelf's server in its <c>mcpServers</c>, by the name
/// <c>modshelf</c>, as <c>{ "command": …, "args": ["mcp"], "env": {…} }</c>. Adding it keeps the file's other servers and
/// settings, keeps a copy of the file as it was beside it, and replaces the file in one step (ADR 0080).
/// </summary>
internal sealed class ClaudeDesktopConfigFile(string? folder) : IClaudeDesktopConfig
{
    public const string FileName = "claude_desktop_config.json";

    /// <summary>
    /// The copy of the file as it was before ModShelf last added its server.
    /// </summary>
    public const string BackupName = "claude_desktop_config.modshelf-backup.json";

    private const string Servers = "mcpServers";

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true };

    public string? FilePath => folder is null ? null : Path.Combine(folder, FileName);

    public string? BackupPath => folder is null ? null : Path.Combine(folder, BackupName);

    public async Task<ClaudeDesktopServer> ReadAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (folder is null || !Directory.Exists(folder))
        {
            return ClaudeDesktopServer.NotFound;
        }

        JsonObject? file = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
        return file?[Servers] is JsonObject servers && servers[AgentAddress.ServerName] is { } server
            ? JsonNode.DeepEquals(server, Entry(command)) ? ClaudeDesktopServer.Same : ClaudeDesktopServer.Different
            : ClaudeDesktopServer.Absent;
    }

    public async Task AddAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (folder is null || !Directory.Exists(folder))
        {
            throw new InvalidOperationException("Claude Desktop wasn't found on this computer: start it once, then try again.");
        }

        string path = FilePath!;
        JsonObject file = await ReadFileAsync(cancellationToken).ConfigureAwait(false) ?? [];
        JsonObject servers = file[Servers] switch
        {
            null => [],
            JsonObject listed => listed,
            _ => throw new InvalidDataException($"{path} can't be changed: its {Servers} isn't a list of servers by name."),
        };
        servers[AgentAddress.ServerName] = Entry(command);
        file[Servers] = servers;

        if (File.Exists(path))
        {
            File.Copy(path, Path.Combine(folder, BackupName), overwrite: true);
        }

        string written = Path.Combine(folder, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(written, file.ToJsonString(Written) + Environment.NewLine, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(written, path, overwrite: true);
        }
        finally
        {
            File.Delete(written);
        }
    }

    /// <summary>
    /// The server, as Claude Desktop starts it.
    /// </summary>
    internal static JsonObject Entry(AgentCommand command)
    {
        JsonObject entry = new()
        {
            ["command"] = command.Path,
            ["args"] = new JsonArray([.. command.Arguments.Select(argument => (JsonNode?)JsonValue.Create(argument))]),
        };
        if (command.Environment.Count > 0)
        {
            entry["env"] = new JsonObject(command.Environment
                .OrderBy(variable => variable.Key, StringComparer.Ordinal)
                .Select(variable => KeyValuePair.Create(variable.Key, (JsonNode?)JsonValue.Create(variable.Value))));
        }

        return entry;
    }

    /// <summary>
    /// The file's settings; null when there's no file.
    /// </summary>
    private async Task<JsonObject?> ReadFileAsync(CancellationToken cancellationToken)
    {
        string path = FilePath!;
        if (!File.Exists(path))
        {
            return null;
        }

        string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException($"{path} can't be read: it isn't a JSON object, as Claude Desktop writes it.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} can't be read: {exception.Message} Fix it in Claude Desktop (Settings, Developer, Edit Config) first.", exception);
        }
    }
}
