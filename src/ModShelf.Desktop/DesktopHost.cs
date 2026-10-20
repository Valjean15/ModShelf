using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

/// <summary>
/// Builds the generic host for the desktop app: configuration, logging and the DI container.
/// </summary>
internal static class DesktopHost
{
    public static IHost Create(string[] args)
    {
        // Read configuration next to the executable: desktop shortcuts and nxm:// launches
        // start the app with an arbitrary working directory.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Console and debugger output only for now; the rolling Serilog file sink from the
        // plan's tech stack arrives with Infrastructure logging.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        builder.Services.AddSingleton<IHostLifetime, DesktopHostLifetime>();

        builder.Services.AddTransient<MainWindowViewModel>();

        return builder.Build();
    }
}
