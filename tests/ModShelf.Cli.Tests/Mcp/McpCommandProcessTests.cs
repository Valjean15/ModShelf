using System.Globalization;

using Microsoft.Data.Sqlite;

using ModelContextProtocol.Client;

using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>modshelf mcp</c> as an agent's app starts it: a process of its own, spoken to over its standard input and
/// output, which carry nothing but the protocol.
/// </summary>
public sealed class McpCommandProcessTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task AnAgentsAppStartsItListsItsToolsAndClosesIt()
    {
        List<string> errors = [];
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = "modshelf",
            Command = CliProcess.DotnetHost(),
            Arguments = ["exec", CliProcess.CliAssembly, "mcp"],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["ModShelf__DatabasePath"] = Path.Combine(_folder.Path, "library.db"),
                ["ModShelf__SecretsFolder"] = Path.Combine(_folder.Path, "Secrets"),
                ["ModShelf__SecretScope"] = string.Create(CultureInfo.InvariantCulture, $"ModShelf.Tests.{Guid.NewGuid():N}"),
            },
            StandardErrorLines = line =>
            {
                lock (errors)
                {
                    errors.Add(line);
                }
            },
        });

        await using (McpClient client = await McpClient.CreateAsync(transport, cancellationToken: Cancellation).WaitAsync(TimeSpan.FromSeconds(60), Cancellation))
        {
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Cancellation);
            ModelContextProtocol.Protocol.CallToolResult settings = await client.CallToolAsync("settings_show", cancellationToken: Cancellation);

            Assert.Equal(19, tools.Count);
            Assert.Equal(ModShelfMcpServer.Instructions, client.ServerInstructions);
            Assert.NotEqual(true, settings.IsError);
            Assert.Contains(Path.Combine(_folder.Path, "settings.json").Replace("\\", "\\\\", StringComparison.Ordinal), settings.StructuredContent?.ToString(), StringComparison.Ordinal);
        }

        // Closing it ended the process; it wrote nothing on standard error at the CLI's console level, warnings.
        lock (errors)
        {
            Assert.Empty(errors);
        }

        Assert.True(File.Exists(Path.Combine(_folder.Path, "library.db")));
    }
}
