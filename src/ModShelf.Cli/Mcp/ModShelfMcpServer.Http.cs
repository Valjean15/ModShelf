using System.Diagnostics;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Agents;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP server at its address, for agents' apps that reach servers by address rather than start them: Streamable
/// HTTP at <c>http://127.0.0.1:&lt;port&gt;/mcp</c>, on this computer alone, answering any number of agents, each request
/// on its own (stateless). The desktop app runs it while agents may use ModShelf (ADR 0080).
/// </summary>
internal static partial class ModShelfMcpServer
{
    /// <summary>
    /// What a request from elsewhere than this computer's apps is answered, with 403.
    /// </summary>
    internal const string NotLocal = "ModShelf's MCP server answers the apps on this computer alone.";

    /// <summary>
    /// Serves the server at its address until <paramref name="cancellationToken"/> is cancelled, or the process
    /// <paramref name="parent"/> names ends.
    /// </summary>
    /// <param name="port">The port; null for <c>agents.port</c>'s, and 0 for any free one, which the output says.</param>
    /// <param name="parent">The ID of the process whose end ends the server, such as the desktop app's; null for none.</param>
    public static async Task<int> RunHttpAsync(CliEnvironment environment, int? port, int? parent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IServiceProvider services = host.Services;
            ILoggerFactory loggers = services.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggers.CreateLogger(typeof(ModShelfMcpServer).FullName!);
            int chosen = port ?? (await services.GetRequiredService<ISettingsService>().GetAsync(cancellationToken).ConfigureAwait(false)).AgentsPort
                ?? AppSettings.DefaultAgentsPort;
            using McpSdkLogging sdkLogging = new(loggers);
            McpQueue queue = new(
                services.GetRequiredService<IQueueOwner>(),
                () => environment.BuildHost(true),
                TimeSpan.FromSeconds(2),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<McpQueue>>());
            await using (queue.ConfigureAwait(false))
            {
                WebApplication app = Build(services, queue, sdkLogging, chosen);
                await using (app.ConfigureAwait(false))
                {
                    try
                    {
                        await app.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (IOException exception)
                    {
                        // Kestrel can't bind the port: another app has it.
                        LogPortInUse(logger, chosen, exception);
                        await environment.Error.WriteLineAsync(
                            $"Port {chosen} is in use by another app, so ModShelf's MCP server can't be served at {AgentAddress.Of(chosen)}. "
                            + "Choose another port in ModShelf's Settings, Agents, or with settings set agents.port.").ConfigureAwait(false);
                        return ExitCodes.PortInUse;
                    }

                    Uri address = AgentAddress.Of(BoundPort(app, chosen));
                    await environment.Output.WriteLineAsync($"ModShelf's MCP server is at {address}").ConfigureAwait(false);
                    await environment.Output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    LogServing(logger, address);
                    await UntilEndAsync(app, parent, logger, cancellationToken).ConfigureAwait(false);
                    await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    LogNoLongerServing(logger);
                }
            }

            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="request"/> comes from an app on this computer: addressed to this computer's own name for
    /// it, at <paramref name="port"/>, and, from a web page, from one of this computer's own. A web page elsewhere that
    /// names another site this computer, so as to reach its servers, says so in its Host or Origin header.
    /// </summary>
    internal static bool IsLocal(HttpRequest request, int port)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Host.Port != port || !IsLocalName(request.Host.Host))
        {
            return false;
        }

        string? origin = request.Headers.Origin;
        return string.IsNullOrEmpty(origin)
            || (Uri.TryCreate(origin, UriKind.Absolute, out Uri? from)
                && (from.Scheme == Uri.UriSchemeHttp || from.Scheme == Uri.UriSchemeHttps)
                && IsLocalName(from.Host));
    }

    private static bool IsLocalName(string host)
        => host is "127.0.0.1" or "[::1]" or "::1" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The web host of the address: Kestrel on 127.0.0.1 alone, the MCP SDK's Streamable HTTP at <see cref="AgentAddress.Path"/>,
    /// and the check that a request comes from this computer before it.
    /// </summary>
    private static WebApplication Build(IServiceProvider services, McpQueue queue, McpSdkLogging sdkLogging, int port)
    {
        WebApplicationBuilder web = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        web.Logging.ClearProviders();
        web.Services.AddSingleton<ILoggerFactory>(sdkLogging);
        web.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        web.Services
            .AddMcpServer(options => Configure(options, services, queue))
            .WithHttpTransport(http => http.Stateless = true);

        WebApplication app = web.Build();
        ILogger logger = sdkLogging.CreateLogger(typeof(ModShelfMcpServer).FullName!);
        app.Use(async (context, next) =>
        {
            if (!IsLocal(context.Request, BoundPort(app, port)))
            {
                LogNotLocal(logger);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync(NotLocal, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapMcp(AgentAddress.Path);
        return app;
    }

    /// <summary>
    /// The port the address has: <paramref name="asked"/>, or the one Kestrel chose for 0.
    /// </summary>
    private static int BoundPort(WebApplication app, int asked)
    {
        if (asked != 0)
        {
            return asked;
        }

        string? bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        return bound is not null && Uri.TryCreate(bound, UriKind.Absolute, out Uri? address) ? address.Port : asked;
    }

    /// <summary>
    /// Waits until the host stops, <paramref name="cancellationToken"/> is cancelled, or the parent ends.
    /// </summary>
    private static async Task UntilEndAsync(WebApplication app, int? parent, ILogger logger, CancellationToken cancellationToken)
    {
        using CancellationTokenSource ended = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task stopped = app.WaitForShutdownAsync(ended.Token);
        Task gone = parent is { } id ? WhileRunningAsync(id, ended.Token) : Task.Delay(Timeout.Infinite, ended.Token);
        if (await Task.WhenAny(stopped, gone).ConfigureAwait(false) == gone && gone.IsCompletedSuccessfully && parent is { } parentId)
        {
            LogParentEnded(logger, parentId);
        }

        await ended.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(stopped, gone).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ended as asked.
        }
    }

    /// <summary>
    /// Waits until the process <paramref name="id"/> names ends; at once when there's none.
    /// </summary>
    private static async Task WhileRunningAsync(int id, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
