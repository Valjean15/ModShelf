using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>mcp --http</c>: the MCP server at its address, as the desktop app runs it while agents may use ModShelf, a process
/// of its own: Streamable HTTP on 127.0.0.1, for this computer's apps alone.
/// </summary>
public sealed partial class McpHttpTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task AnAgentsAppReachesItAtItsAddress()
    {
        AllowAgents();
        using Server server = await Server.StartAsync(this, ["--port", "0"]);

        await using McpClient client = await McpClient
            .CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = server.Address, TransportMode = HttpTransportMode.StreamableHttp }), cancellationToken: Cancellation)
            .WaitAsync(Patience, Cancellation);
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Cancellation);
        CallToolResult settings = await client.CallToolAsync("settings_show", cancellationToken: Cancellation);

        Assert.Matches(@"^http://127\.0\.0\.1:\d+/mcp$", server.Address.ToString());
        Assert.Equal(26, tools.Count);
        Assert.Equal(ModShelfMcpServer.Instructions, client.ServerInstructions);
        Assert.NotEqual(true, settings.IsError);
    }

    [Fact]
    public async Task OffItRefusesEveryCall()
    {
        using Server server = await Server.StartAsync(this, ["--port", "0"]);

        await using McpClient client = await McpClient
            .CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = server.Address, TransportMode = HttpTransportMode.StreamableHttp }), cancellationToken: Cancellation)
            .WaitAsync(Patience, Cancellation);
        CallToolResult refused = await client.CallToolAsync("library_list", cancellationToken: Cancellation);

        Assert.True(refused.IsError);
        Assert.EndsWith(AgentAccess.Off, Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Origin", "https://pages.example")]
    [InlineData("Origin", "null")]
    [InlineData("Host", "pages.example")]
    public async Task ARequestFromElsewhereIsRefused(string header, string value)
    {
        AllowAgents();
        using Server server = await Server.StartAsync(this, ["--port", "0"]);
        using HttpClient http = new();
        using HttpRequestMessage request = new(HttpMethod.Post, server.Address)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (header == "Host")
        {
            request.Headers.Host = value;
        }
        else
        {
            request.Headers.Add(header, value);
        }

        using HttpResponseMessage response = await http.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ModShelfMcpServer.NotLocal, await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("localhost", null, true)]
    [InlineData("LOCALHOST", "http://localhost:5173", true)]
    [InlineData("127.0.0.1", "https://127.0.0.1", true)]
    [InlineData("127.0.0.1", "http://[::1]:8080", true)]
    [InlineData("127.0.0.1", "https://pages.example", false)]
    [InlineData("127.0.0.1", "file://", false)]
    [InlineData("pages.example", null, false)]
    [InlineData("127.0.0.1.pages.example", null, false)]
    public void OnlyThisComputersAppsAreLocal(string? host, string? origin, bool local)
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString(host ?? "127.0.0.1", 47823);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        Assert.Equal(local, ModShelfMcpServer.IsLocal(context.Request, 47823));
        Assert.False(ModShelfMcpServer.IsLocal(context.Request, 47824));
    }

    [Fact]
    public async Task APortInUseIsSaid()
    {
        using TcpListener taken = new(IPAddress.Loopback, 0);
        taken.Start();
        int port = ((IPEndPoint)taken.LocalEndpoint).Port;

        using Process process = Start(["mcp", "--http", "--port", port.ToString(CultureInfo.InvariantCulture)]);
        Task<string> errors = process.StandardError.ReadToEndAsync(Cancellation);
        await process.WaitForExitAsync(Cancellation).WaitAsync(Patience, Cancellation);

        Assert.Equal(4, process.ExitCode);
        Assert.Contains($"Port {port} is in use by another app, so ModShelf's MCP server can't be served at http://127.0.0.1:{port}/mcp.", await errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItEndsWithTheProcessItRunsFor()
    {
        using Process parent = Start(["mcp"]);
        using Server server = await Server.StartAsync(this, ["--port", "0", "--parent", parent.Id.ToString(CultureInfo.InvariantCulture)]);

        parent.Kill(entireProcessTree: true);
        await server.Process.WaitForExitAsync(Cancellation).WaitAsync(Patience, Cancellation);

        Assert.Equal(0, server.Process.ExitCode);
    }

    [Fact]
    public async Task ThePortComesFromTheSettings()
    {
        using TcpListener free = new(IPAddress.Loopback, 0);
        free.Start();
        int port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        File.WriteAllText(SettingsFile, $$"""{ "agents": { "port": {{port}} } }""");

        using Server server = await Server.StartAsync(this, []);

        Assert.Equal(new Uri($"http://127.0.0.1:{port}/mcp"), server.Address);
    }

    private string SettingsFile => Path.Combine(_folder.Path, "settings.json");

    [GeneratedRegex(@"is at (?<address>http://\S+)")]
    private static partial Regex Serving();

    private void AllowAgents() => File.WriteAllText(SettingsFile, """{ "agents": { "enabled": true } }""");

    private Process Start(IEnumerable<string> arguments)
    {
        ProcessStartInfo start = new(CliProcess.DotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(CliProcess.CliAssembly);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ModShelf__DatabasePath"] = Path.Combine(_folder.Path, "library.db");
        start.Environment["ModShelf__SecretsFolder"] = Path.Combine(_folder.Path, "Secrets");
        start.Environment["ModShelf__SecretScope"] = string.Create(CultureInfo.InvariantCulture, $"ModShelf.Tests.{Guid.NewGuid():N}");
        return Process.Start(start) ?? throw new InvalidOperationException("The CLI didn't start.");
    }

    /// <summary>
    /// <c>mcp --http</c> running, once it said its address; ended when disposed.
    /// </summary>
    private sealed class Server(Process process, Uri address) : IDisposable
    {
        public Process Process { get; } = process;

        public Uri Address { get; } = address;

        public static async Task<Server> StartAsync(McpHttpTests tests, IEnumerable<string> arguments)
        {
            Process process = tests.Start(["mcp", "--http", .. arguments]);
            try
            {
                _ = process.StandardError.ReadToEndAsync(Cancellation);
                while (await process.StandardOutput.ReadLineAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation) is { } line)
                {
                    if (Serving().Match(line) is { Success: true } said)
                    {
                        _ = process.StandardOutput.ReadToEndAsync(Cancellation);
                        return new Server(process, new Uri(said.Groups["address"].Value));
                    }
                }

                throw new InvalidOperationException($"mcp --http ended without an address, with exit code {process.ExitCode}.");
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                process.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(10_000);
            }

            Process.Dispose();
        }
    }
}
