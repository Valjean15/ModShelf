using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application;
using ModShelf.Desktop.Services;
using ModShelf.Hosting;
using ModShelf.Platform;
using ModShelf.Presentation;
using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Hosting;

/// <summary>
/// The desktop app's composition root: the generic host with every service the CLI has, plus the ones that run in
/// the background while the app is open (downloads, recording them into the library, and update checks), and the
/// screens.
/// </summary>
internal static class DesktopHost
{
    /// <summary>
    /// What the desktop app's log files' names start with, such as <c>desktop-20261029.log</c>.
    /// </summary>
    private const string LogFilePrefix = "desktop";

    public static IHost Create(string[] args)
    {
        // Read configuration next to the executable: desktop shortcuts and nxm:// launches
        // start the app with an arbitrary working directory. Only --key=value arguments are
        // configuration: a link the app is started with, which can carry a key, is not.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [.. args.Where(arg => arg.StartsWith("--", StringComparison.Ordinal))],
            ContentRootPath = AppContext.BaseDirectory,
        });

        // A service missing a dependency fails at start, not when its screen first opens.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        // The console and the debugger, and a log file a day in a logs folder beside the database (below). The plan's
        // tech stack named Serilog for the files; a small provider of ModShelf's own writes them without another package.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        builder.Services.AddSingleton<IHostLifetime, DesktopHostLifetime>();

        // What the CLI has too, then one app at a time for each library, and the tray.
        string folder = builder.Services.AddModShelf(builder.Configuration, LogFilePrefix);
        builder.Services.AddSingleInstance(folder);
        builder.Services.AddTrayHost();

        // While the app is open, queued jobs download, finished ones are recorded into the library, and the watched
        // mods are checked for updates every updates.interval.
        builder.Services.AddBackgroundDownloads();
        builder.Services.AddLibraryRecording();
        builder.Services.AddBackgroundUpdateChecks();

        builder.Services.AddPresentation();
        builder.Services.AddSingleton<IUiThread, AvaloniaUiThread>();
        builder.Services.AddSingleton<IDialogService, AvaloniaDialogService>();
        builder.Services.AddSingleton<IFolderOpener, AvaloniaFolderOpener>();
        builder.Services.AddSingleton<IClipboard, AvaloniaClipboard>();
        builder.Services.AddSingleton<IThemeSwitcher, AvaloniaThemeSwitcher>();

        return builder.Build();
    }
}
