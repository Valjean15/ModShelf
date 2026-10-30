using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using ModShelf.Application;
using ModShelf.Hosting;

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

    /// <param name="runDownloads">
    /// Whether queued jobs download while the host runs. A command that only reads the queue leaves this off.
    /// </param>
    /// <param name="settings">Configuration that takes precedence over appsettings.json and environment variables.</param>
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

            // Read appsettings.json next to the executable, not from whatever directory the shell is in.
            ContentRootPath = AppContext.BaseDirectory,
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        // Logs go to stderr so they never mix with a command's output; appsettings.json keeps them to warnings there,
        // and lets more through to the log files.
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
            options.FormatterName = CliConsoleFormatter.FormatterName;
        });
        builder.Logging.AddConsoleFormatter<CliConsoleFormatter, ConsoleFormatterOptions>();
        builder.Services.AddSingleton<IHostLifetime, CliHostLifetime>();

        builder.Services.AddModShelf(builder.Configuration, LogFilePrefix);
        if (runDownloads)
        {
            builder.Services.AddBackgroundDownloads();
            builder.Services.AddLibraryRecording();
        }

        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}
