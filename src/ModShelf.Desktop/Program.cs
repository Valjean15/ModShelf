using Avalonia;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia APIs or SynchronizationContext-reliant code
    // before StartWithClassicDesktopLifetime runs: Avalonia isn't initialized yet. Starting the
    // generic host first is fine, because hosted services never depend on the UI thread.
    [STAThread]
    public static int Main(string[] args)
    {
        using IHost host = DesktopHost.Create(args);
        host.Start();

        try
        {
            return CreateAppBuilder(host.Services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Avalonia leaves its SynchronizationContext on this thread after its dispatcher has
            // stopped. Clear it, or a continuation in a hosted service's StopAsync (or in an async
            // Dispose) would be posted to the dead dispatcher and the process would never exit.
            SynchronizationContext.SetSynchronizationContext(null);

            // Avalonia needs an STA thread on Windows, so Main stays synchronous and stops the
            // host through its synchronous helpers: WaitForShutdown waits for StopApplication.
            host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            host.WaitForShutdown();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => Configure(AppBuilder.Configure<App>());

    private static AppBuilder CreateAppBuilder(IServiceProvider services)
        => Configure(AppBuilder.Configure(
            () => new App(() => services.GetRequiredService<MainWindowViewModel>())));

    private static AppBuilder Configure(AppBuilder builder)
        => builder
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
