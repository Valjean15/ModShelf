using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using ModShelf.Application;
using ModShelf.Cli.Commands;
using ModShelf.Hosting;
using ModShelf.Platform;

namespace ModShelf.Cli.Hosting;

/// <summary>
/// The CLI's composition root: builds the generic host with the services the desktop app has too
/// (<see cref="HostServices"/>), and runs downloads only when a command asks.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// What the CLI's log files' names start with, such as <c>cli-20261029.log</c>. They sit beside the desktop app's.
    /// </summary>
    private const string LogFilePrefix = "cli";

    /// <summary>
    /// The CLI's settings file, beside it. The desktop app's are appsettings.json, which an installed ModShelf has in the
    /// same folder (ADR 0040).
    /// </summary>
    internal const string SettingsFile = "appsettings.cli.json";

    /// <param name="runDownloads">
    /// Whether queued jobs download while the host runs. A command that only reads the queue leaves this off.
    /// </param>
    /// <param name="settings">Configuration that takes precedence over the settings files and environment variables.</param>
    /// <param name="configureServices">Runs after every registration, so it can add services or replace them.</param>
    public static IHost Build(
        bool runDownloads,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // System.CommandLine owns the arguments, so they aren't read as configuration too.
            Args = [],

            // Read the settings files next to the executable, not from whatever directory the shell is in.
            ContentRootPath = AppContext.BaseDirectory,
        });
        AddCliSettings(builder.Configuration, AppContext.BaseDirectory);
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        // Logs go to stderr so they never mix with a command's output; appsettings.cli.json keeps them to warnings there,
        // and lets more through to the log files.
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
            options.FormatterName = CliConsoleFormatter.FormatterName;
        });
        builder.Logging.AddConsoleFormatter<CliConsoleFormatter, ConsoleFormatterOptions>();
        builder.Services.AddSingleton<IHostLifetime, CliHostLifetime>();

        // Its first log line says what this process runs, before the library database opens (ADR 0081).
        builder.Services.AddProcessStartLog(RunningCommand.Description);

        // The desktop app, when it runs this library's queue, carries out the queue requests of a host that doesn't.
        string folder = builder.Services.AddModShelf(builder.Configuration, LogFilePrefix);
        builder.Services.AddQueueOwner(folder);
        if (runDownloads)
        {
            builder.Services.AddBackgroundDownloads();
            builder.Services.AddLibraryRecording();
        }

        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }

    /// <summary>
    /// Reads the CLI's settings file in <paramref name="folder"/> right after appsettings.json, whose settings it takes
    /// precedence over: in an installed ModShelf, the desktop app's. The environment, and what's added after, still
    /// take precedence over it.
    /// </summary>
    internal static void AddCliSettings(IConfigurationBuilder configuration, string folder)
    {
        IList<IConfigurationSource> sources = configuration.Sources;
        int appsettings = -1;
        for (int index = 0; index < sources.Count; index++)
        {
            if (sources[index] is JsonConfigurationSource { Path: "appsettings.json" })
            {
                appsettings = index;
            }
        }

        JsonConfigurationSource cli = new() { Path = Path.Combine(folder, SettingsFile), Optional = true };
        cli.ResolveFileProvider();
        sources.Insert(appsettings + 1, cli);
    }
}
