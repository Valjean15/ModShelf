using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Hosting;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// <c>mcp</c> running in this process, with an MCP client connected to its standard input and output, as an agent's
/// app connects to it: the client's messages go down one pipe, the server's come back up another. Each result is
/// checked against its tool's output schema, as some clients check it.
/// </summary>
internal sealed class McpSession : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Pipe _toServer = new();
    private readonly Pipe _fromServer = new();
    private readonly CliRun _run;
    private McpClient? _client;
    private Dictionary<string, JsonNode>? _outputSchemas;

    private McpSession(Func<bool, IHost> buildHost)
    {
        _run = new CliRun(["mcp"], TextReader.Null, buildHost, () => new StandardStreams(_toServer.Reader.AsStream(), _fromServer.Writer.AsStream()));
    }

    public McpClient Client => _client ?? throw new InvalidOperationException("The client isn't connected.");

    public static async Task<McpSession> StartAsync(Func<bool, IHost> buildHost, CancellationToken cancellationToken)
    {
        McpSession session = new(buildHost);
        try
        {
            session._client = await McpClient
                .CreateAsync(new StreamClientTransport(session._toServer.Writer.AsStream(), session._fromServer.Reader.AsStream()), cancellationToken: cancellationToken)
                .WaitAsync(Timeout, cancellationToken)
                .ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Calls <paramref name="tool"/>, and reads its structured result.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool answered with an error.</exception>
    public async Task<T> CallAsync<T>(string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        CallToolResult result = await CallRawAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"{tool} failed: {Text(result)}");
        }

        JsonNode content = JsonSerializer.SerializeToNode(result.StructuredContent, McpJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException($"{tool} gave no structured content.");

        // As clients that check results do.
        _outputSchemas ??= (await Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .ToDictionary(listed => listed.Name, listed => JsonSerializer.SerializeToNode(listed.ProtocolTool.OutputSchema, McpJsonUtilities.DefaultOptions)!);
        Assert.Empty(OutputSchemas.Problems(content, _outputSchemas[tool]));

        return content.Deserialize<T>(McpJsonUtilities.DefaultOptions) ?? throw new InvalidOperationException($"{tool} gave null.");
    }

    /// <summary>
    /// Calls <paramref name="tool"/>, expecting an error, and gives its text.
    /// </summary>
    public async Task<string> CallFailingAsync(string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        CallToolResult result = await CallRawAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        Assert.True(result.IsError, $"{tool} didn't fail: {Text(result)}");
        return Text(result);
    }

    public Task<CallToolResult> CallRawAsync(string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
        => Client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).AsTask().WaitAsync(Timeout, cancellationToken);

    /// <summary>
    /// Closes the client, as an agent's app does, which ends the server's input; then waits for the command to end.
    /// </summary>
    public async Task<CliResult> CloseAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        await _toServer.Writer.CompleteAsync().ConfigureAwait(false);
        return await _run.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        await _toServer.Writer.CompleteAsync().ConfigureAwait(false);
        await _run.DisposeAsync().ConfigureAwait(false);
        await _fromServer.Reader.CompleteAsync().ConfigureAwait(false);
    }

    private static string Text(CallToolResult result)
        => string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
