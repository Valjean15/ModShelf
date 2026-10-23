using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using ModShelf.Application;
using ModShelf.Infrastructure;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.DirectUrl;

namespace ModShelf.Cli.Hosting;

/// <summary>
/// The CLI's composition root: builds the generic host with the same services the desktop app will use.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// The configuration key, or <c>ModShelf__DatabasePath</c> as an environment variable, that moves the
    /// library database away from its default location.
    /// </summary>
    public const string DatabasePathKey = "ModShelf:DatabasePath";

    /// <param name="runDownloads">
    /// Whether queued jobs download while the host runs. A command that only reads the queue leaves this off.
    /// </param>
    public static IHost Build(bool runDownloads)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // System.CommandLine owns the arguments, so they aren't read as configuration too.
            Args = [],

            // Read appsettings.json next to the executable, not from whatever directory the shell is in.
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Logs go to stderr so they never mix with a command's output; appsettings.json keeps them to warnings.
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IHostLifetime, CliHostLifetime>();

        string? configuredPath = builder.Configuration[DatabasePathKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            builder.Services.AddLibraryDatabase();
        }
        else
        {
            builder.Services.AddLibraryDatabase(Path.GetFullPath(configuredPath));
        }

        builder.Services.AddHttpFileTransfer();
        builder.Services.AddDownloadEngine();
        builder.Services.AddSingleton<IModSource, DirectUrlSource>();
        if (runDownloads)
        {
            builder.Services.AddBackgroundDownloads();
        }

        return builder.Build();
    }
}
