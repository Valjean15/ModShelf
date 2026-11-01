using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>modshelf mcp</c> as an agent's app starts it: a process of its own, spoken to over its standard input and
/// output, which carry nothing but the protocol.
/// </summary>
/// <remarks>
/// The test starts the process itself, as the agents' apps do. The SDK's own <see cref="StdioClientTransport"/> starts
/// a server through <c>cmd.exe /c</c> on Windows, which can't start a program whose path has a space in it, such as
/// <c>C:\Program Files\dotnet\dotnet.exe</c>, with arguments that have spaces too.
/// </remarks>
public sealed class McpCommandProcessTests : IDisposable
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
    public async Task AnAgentsAppStartsItListsItsToolsAndClosesIt()
    {
        List<string> errors = [];
        using Process server = Start(errors);
        await using RecordingStream output = new(server.StandardOutput.BaseStream);
        try
        {
            await using (McpClient client = await McpClient
                .CreateAsync(new StreamClientTransport(server.StandardInput.BaseStream, output), cancellationToken: Cancellation)
                .WaitAsync(Patience, Cancellation))
            {
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Cancellation);
                CallToolResult settings = await client.CallToolAsync("settings_show", cancellationToken: Cancellation);

                Assert.Equal(19, tools.Count);
                Assert.Equal(ModShelfMcpServer.Instructions, client.ServerInstructions);
                Assert.NotEqual(true, settings.IsError);
                Assert.Equal(
                    Path.Combine(_folder.Path, "settings.json"),
                    JsonSerializer.SerializeToNode(settings.StructuredContent, McpJsonUtilities.DefaultOptions)?["location"]?.GetValue<string>());
            }

            // Closing its input, as the agent's app does when it's done, ends it.
            server.StandardInput.Close();
            await server.WaitForExitAsync(Cancellation).WaitAsync(Patience, Cancellation);
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
            }
        }

        Assert.Equal(0, server.ExitCode);

        // Each line it wrote on standard output is a JSON-RPC message: the SDK's client passes over the others.
        string[] lines = output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal("2.0", JsonDocument.Parse(line).RootElement.GetProperty("jsonrpc").GetString()));

        // It wrote nothing on standard error at the CLI's console level, warnings.
        server.WaitForExit();
        lock (errors)
        {
            Assert.Empty(errors);
        }

        Assert.True(File.Exists(Path.Combine(_folder.Path, "library.db")));
    }

    /// <summary>
    /// Starts <c>mcp</c> on the CLI built beside these tests, with the library in the test's folder, recording each
    /// line it writes on standard error.
    /// </summary>
    private Process Start(List<string> errors)
    {
        ProcessStartInfo start = new(CliProcess.DotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "exec", CliProcess.CliAssembly, "mcp" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ModShelf__DatabasePath"] = Path.Combine(_folder.Path, "library.db");
        start.Environment["ModShelf__SecretsFolder"] = Path.Combine(_folder.Path, "Secrets");
        start.Environment["ModShelf__SecretScope"] = string.Create(CultureInfo.InvariantCulture, $"ModShelf.Tests.{Guid.NewGuid():N}");

        Process server = new() { StartInfo = start };
        server.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is { } text)
            {
                lock (errors)
                {
                    errors.Add(text);
                }
            }
        };
        if (!server.Start())
        {
            server.Dispose();
            throw new InvalidOperationException("The CLI didn't start.");
        }

        server.BeginErrorReadLine();
        return server;
    }

    /// <summary>
    /// A stream read through as it is, keeping what was read.
    /// </summary>
    private sealed class RecordingStream(Stream inner) : Stream
    {
        private readonly MemoryStream _read = new();

        public string Text
        {
            get
            {
                lock (_read)
                {
                    return System.Text.Encoding.UTF8.GetString(_read.ToArray());
                }
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Keep(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            return Keep(buffer.Span[..read]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _read.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Keep(ReadOnlySpan<byte> read)
        {
            lock (_read)
            {
                _read.Write(read);
            }

            return read.Length;
        }
    }
}
