using System.Text.Json.Nodes;

using ModShelf.Application.Agents.Ports;
using ModShelf.Platform.Agents;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Agents;

/// <summary>
/// ModShelf's server in Claude Desktop's <c>claude_desktop_config.json</c>: found or not, added keeping everything else
/// and a copy of the file as it was, and a file Claude Desktop couldn't read left as it is.
/// </summary>
public sealed class ClaudeDesktopConfigFileTests : IDisposable
{
    private static readonly AgentCommand Command = new(
        "/opt/ModShelf/ModShelf.Cli",
        ["mcp"],
        new Dictionary<string, string> { ["ModShelf__DatabasePath"] = "/home/kestrel/Mods test/library.db" });

    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Claude => Path.Combine(_folder.Path, "Claude");

    private string FilePath => Path.Combine(Claude, "claude_desktop_config.json");

    private string BackupPath => Path.Combine(Claude, "claude_desktop_config.modshelf-backup.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ClaudeDesktopIsntFoundWithoutItsFolder()
    {
        ClaudeDesktopConfigFile config = new(Claude);

        Assert.Equal(ClaudeDesktopServer.NotFound, await config.ReadAsync(Command, Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => config.AddAsync(Command, Cancellation));
        Assert.False(Directory.Exists(Claude));
        Assert.Equal(ClaudeDesktopServer.NotFound, await new ClaudeDesktopConfigFile(null).ReadAsync(Command, Cancellation));
        Assert.Null(new ClaudeDesktopConfigFile(null).FilePath);
    }

    [Fact]
    public async Task AddsItToANewFile()
    {
        Directory.CreateDirectory(Claude);
        ClaudeDesktopConfigFile config = new(Claude);
        Assert.Equal(ClaudeDesktopServer.Absent, await config.ReadAsync(Command, Cancellation));

        await config.AddAsync(Command, Cancellation);

        Assert.Equal((FilePath, BackupPath), (config.FilePath, config.BackupPath));
        JsonNode server = JsonNode.Parse(await File.ReadAllTextAsync(FilePath, Cancellation))!["mcpServers"]!["modshelf"]!;
        Assert.Equal(
            """{"command":"/opt/ModShelf/ModShelf.Cli","args":["mcp"],"env":{"ModShelf__DatabasePath":"/home/kestrel/Mods test/library.db"}}""",
            server.ToJsonString());
        Assert.Equal(ClaudeDesktopServer.Same, await config.ReadAsync(Command, Cancellation));
        Assert.False(File.Exists(BackupPath));
        Assert.Single(Directory.GetFiles(Claude));
    }

    [Fact]
    public async Task KeepsTheOtherServersAndSettingsAndACopyOfTheFile()
    {
        Directory.CreateDirectory(Claude);
        const string Before = """
            {
              "globalShortcut": "Ctrl+Space",
              "mcpServers": {
                "filesystem": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\kestrel\\Desktop"] }
              },
              "preferences": { "later": true }
            }
            """;
        await File.WriteAllTextAsync(FilePath, Before, Cancellation);
        ClaudeDesktopConfigFile config = new(Claude);

        await config.AddAsync(Command, Cancellation);

        JsonNode after = JsonNode.Parse(await File.ReadAllTextAsync(FilePath, Cancellation))!;
        Assert.Equal("Ctrl+Space", after["globalShortcut"]!.GetValue<string>());
        Assert.True(after["preferences"]!["later"]!.GetValue<bool>());
        Assert.Equal(["filesystem", "modshelf"], after["mcpServers"]!.AsObject().Select(server => server.Key));
        Assert.Equal("C:\\Users\\kestrel\\Desktop", after["mcpServers"]!["filesystem"]!["args"]![2]!.GetValue<string>());
        Assert.Equal(Before, await File.ReadAllTextAsync(BackupPath, Cancellation));
    }

    [Fact]
    public async Task ReplacesAnotherServerByItsName()
    {
        Directory.CreateDirectory(Claude);
        await File.WriteAllTextAsync(FilePath, """{ "mcpServers": { "modshelf": { "command": "C:\\Old\\ModShelf.Cli.exe", "args": ["mcp"] } } }""", Cancellation);
        ClaudeDesktopConfigFile config = new(Claude);
        Assert.Equal(ClaudeDesktopServer.Different, await config.ReadAsync(Command, Cancellation));
        Assert.Equal(ClaudeDesktopServer.Different, await config.ReadAsync(Command with { Environment = new Dictionary<string, string>() }, Cancellation));

        await config.AddAsync(Command, Cancellation);

        Assert.Equal(ClaudeDesktopServer.Same, await config.ReadAsync(Command, Cancellation));
        Assert.Contains("C:\\\\Old\\\\ModShelf.Cli.exe", await File.ReadAllTextAsync(BackupPath, Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"mcpServers\": { ")]
    [InlineData("[]")]
    [InlineData("{ \"mcpServers\": [] }")]
    public async Task AFileClaudeDesktopCouldntReadIsLeftAsItIs(string content)
    {
        Directory.CreateDirectory(Claude);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
        ClaudeDesktopConfigFile config = new(Claude);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => config.AddAsync(Command, Cancellation));

        Assert.StartsWith($"{FilePath} can", refused.Message, StringComparison.Ordinal);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath, Cancellation));
        Assert.False(File.Exists(BackupPath));
        Assert.Single(Directory.GetFiles(Claude));
    }

    [Fact]
    public async Task AnEmptyFileIsAFileWithoutServers()
    {
        Directory.CreateDirectory(Claude);
        await File.WriteAllTextAsync(FilePath, "  ", Cancellation);
        ClaudeDesktopConfigFile config = new(Claude);

        Assert.Equal(ClaudeDesktopServer.Absent, await config.ReadAsync(Command, Cancellation));
        await config.AddAsync(Command, Cancellation);

        Assert.Equal(ClaudeDesktopServer.Same, await config.ReadAsync(Command, Cancellation));
    }
}
