using Microsoft.Extensions.Logging;

using ModelContextProtocol;

using ModShelf.Cli.Mcp;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// The MCP SDK logs through ModShelf's logging only its warnings and errors, and none that carries a message's
/// content or is a tool's answer to the agent.
/// </summary>
public sealed class McpSdkLoggingTests : IDisposable
{
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _host;
    private readonly McpSdkLogging _logging;

    public McpSdkLoggingTests()
    {
        _host = LoggerFactory.Create(builder => builder.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        _logging = new McpSdkLogging(_host);
    }

    public void Dispose()
    {
        _logging.Dispose();
        _host.Dispose();
        _logs.Dispose();
    }

    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, false)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    public void OnlyWarningsAndErrorsGetThrough(LogLevel level, bool logged)
    {
        ILogger logger = _logging.CreateLogger("ModelContextProtocol.Server.McpServer");

        Log(logger, level, ("EndpointName", "Server (modshelf)"), ("Method", "tools/call"));

        Assert.Equal(logged, logger.IsEnabled(level));
        Assert.Equal(logged ? 1 : 0, _logs.Entries.Count);
    }

    [Theory]
    [InlineData("Message")]
    [InlineData("Request")]
    [InlineData("Response")]
    [InlineData("Arguments")]
    [InlineData("responseContent")]
    public void WhatCarriesAMessagesContentDoesnt(string carried)
    {
        ILogger logger = _logging.CreateLogger("ModelContextProtocol.Protocol.Transport");

        Log(logger, LogLevel.Error, ("EndpointName", "Server (modshelf)"), (carried, "{\"link\":\"nxm://game/mods/1/files/2?key=secret-key\"}"));

        Assert.Empty(_logs.Entries);
    }

    [Fact]
    public void AToolsAnswerToTheAgentIsntLogged()
    {
        ILogger logger = _logging.CreateLogger("ModelContextProtocol.Server.McpServer");

        Log(logger, LogLevel.Error, new McpException("No download has that ID."), ("ToolName", "downloads_pause"));
        Log(logger, LogLevel.Error, new InvalidOperationException("A bug."), ("ToolName", "downloads_pause"));

        string entry = Assert.Single(_logs.Entries);
        Assert.Contains("A bug.", entry, StringComparison.Ordinal);
        Assert.StartsWith("[ModelContextProtocol.Server.McpServer]", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void ItsProvidersAreTheHosts()
    {
        Assert.Throws<NotSupportedException>(() => _logging.AddProvider(_logs));
    }

    private static void Log(ILogger logger, LogLevel level, params (string Key, object? Value)[] values)
        => Log(logger, level, null, values);

    private static void Log(ILogger logger, LogLevel level, Exception? exception, params (string Key, object? Value)[] values)
    {
        IReadOnlyList<KeyValuePair<string, object?>> state = [.. values.Select(value => new KeyValuePair<string, object?>(value.Key, value.Value))];
        logger.Log(level, new EventId(1), state, exception, (logged, _) => string.Join(" ", logged.Select(value => value.Value)));
    }
}
