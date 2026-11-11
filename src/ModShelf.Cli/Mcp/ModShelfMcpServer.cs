using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Agents;
using ModShelf.Application.Diagnostics;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// ModShelf's MCP server, which <c>modshelf mcp</c> runs: the CLI's services as tools an AI agent calls, over standard
/// input and output, until the agent's app closes them (ADR 0040). Logs go to standard error and the log files, never
/// to standard output, which is the protocol's.
/// </summary>
internal static partial class ModShelfMcpServer
{
    /// <summary>
    /// What the server tells the agent about ModShelf when it connects.
    /// </summary>
    internal const string Instructions =
        "ModShelf downloads mods' files from Nexus Mods (nexus), GameBanana (gamebanana) and mod.io (modio) into a "
        + "library folder, keeps their versions, and checks the watched mods for updates. It never installs mods: a mod "
        + "manager installs the files from the library.\n"
        + "- Each game, mod and file is one site's, and every result says which as source; its links are that site's. A "
        + "game is named by its site and its key there: nexus/stardewvalley, gamebanana/16951 or modio/6715, as "
        + "GameBanana and mod.io name their games by number. The same game on two sites is two games, each with its own "
        + "mods, keys and settings: mod_games finds a site's games and their keys, and mod_search searches one site's "
        + "game, so search each site a game is on. A key alone that could be more than one site's needs its site, as "
        + "source.\n"
        + "- library_list lists the library a page at a time, each file with its path, size and SHA-256; compact, a game or "
        + "a mod, and watchedOnly, updatesOnly or missingOnly keep a page small.\n"
        + "- library_updates lists the updates the checks found, each with its link and the library file it replaces; "
        + "library_check checks the sites now.\n"
        + "- download takes a file's or a mod's link, such as an update's, and downloads the file into the library. "
        + "ModShelf's desktop app downloads it when it runs; otherwise this server does, until no download is under way. "
        + "With a free Nexus Mods account, the user starts each download on the site: the download waits, with the page "
        + "to open (downloads_list).\n"
        + "- library_changes lists the files downloaded since a time, each with the older version it replaced, what became "
        + "of it, and its mod's other files in the library, so the newer version can replace an older one where the mods "
        + "are installed.\n"
        + "- downloads_list shows the queue; downloads_pause, downloads_resume, downloads_retry and downloads_cancel "
        + "change it.\n"
        + "- settings_show, settings_set and settings_reset read and change the settings; library.oldVersions decides "
        + "whether old versions stay, are archived or are deleted.\n"
        + "- library_pin pins a mod the user keeps at its version, library_skip skips one update until a newer one comes, and "
        + "library_note keeps the user's note of a mod; library_updates and library_check leave pinned mods' updates and "
        + "skipped updates out.\n"
        + "- library_forget takes a mod out of the library, such as one the user no longer uses; its files stay on disk.\n"
        + "- library_rescan adds files placed by hand in the mods' folders, and reads the hashes the library lacks.\n"
        + "- Linking accounts, deleting files and moving the library are for the user, in the desktop app or the CLI.";

    /// <summary>
    /// How the tools' arguments and results are written: as the SDK writes them, nulls included.
    /// </summary>
    internal static JsonSerializerOptions Json { get; } = new(McpJsonUtilities.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Runs the server until standard input ends, or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        StandardStreams streams = environment.OpenStandardStreams?.Invoke()
            ?? throw new InvalidOperationException("There's no standard input and output to run the MCP server on.");

        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IServiceProvider services = host.Services;
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ModShelfMcpServer).FullName!);
            using McpSdkLogging sdkLogging = new(services.GetRequiredService<ILoggerFactory>());
            McpQueue queue = new(
                services.GetRequiredService<IQueueOwner>(),
                () => environment.BuildHost(true),
                TimeSpan.FromSeconds(2),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<McpQueue>>());
            await using (queue.ConfigureAwait(false))
            {
                McpServerOptions options = new();
                Configure(options, services, queue);

                StreamServerTransport transport = new(streams.Input, streams.Output, AgentAddress.ServerName, sdkLogging);
                await using (transport.ConfigureAwait(false))
                {
                    McpServer server = McpServer.Create(transport, options, sdkLogging, services);
                    await using (server.ConfigureAwait(false))
                    {
                        LogStarted(logger, options.ToolCollection!.Count);
                        await server.RunAsync(cancellationToken).ConfigureAwait(false);
                        LogEnded(logger);
                    }
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
    /// The server's options, over standard input and output or at its address alike: what it says it is, its
    /// instructions and tools, and the checks of each call, whether agents may use ModShelf first.
    /// </summary>
    internal static void Configure(McpServerOptions options, IServiceProvider services, McpQueue queue)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        options.ServerInfo = new Implementation
        {
            Name = AgentAddress.ServerName,
            Title = "ModShelf",
            Version = AppInfo.Of(typeof(ModShelfMcpServer).Assembly).Version,
        };
        options.ServerInstructions = Instructions;
        options.ToolCollection = Tools(services, queue);
        options.Filters.Request.CallToolFilters.Add(AgentAccess.Checked(
            services.GetRequiredService<ISettingsService>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AgentAccess).FullName!)));
        options.Filters.Request.CallToolFilters.Add(ToolArguments.Explained);
    }

    /// <summary>
    /// Every tool of the tool types, made with the host's services.
    /// </summary>
    internal static McpServerPrimitiveCollection<McpServerTool> Tools(IServiceProvider services, McpQueue queue)
    {
        object[] targets =
        [
            ActivatorUtilities.CreateInstance<LibraryTools>(services),
            ActivatorUtilities.CreateInstance<DownloadTools>(services, queue, ActivatorUtilities.CreateInstance<McpDeprecations>(services)),
            ActivatorUtilities.CreateInstance<ModTools>(services),
            ActivatorUtilities.CreateInstance<SettingsTools>(services),
            ActivatorUtilities.CreateInstance<StatusTools>(services),
        ];

        // The output schemas list every property as required, null allowed where it's optional; the SDK's own options
        // leave nulls out of the results, which clients that check results against the schemas refuse. A deprecated
        // argument's input schema says so.
        McpServerToolCreateOptions options = new() { SerializerOptions = Json };
        McpServerPrimitiveCollection<McpServerTool> tools = [];
        foreach (object target in targets)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                {
                    McpServerTool tool = McpServerTool.Create(method, target, options);
                    McpDeprecations.MarkDeprecated(tool.ProtocolTool, method);
                    tools.Add(tool);
                }
            }
        }

        return tools;
    }
}
