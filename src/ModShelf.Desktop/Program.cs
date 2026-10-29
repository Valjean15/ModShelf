using System.Globalization;
using System.Text;

using Avalonia;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Desktop.Hosting;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

internal static partial class Program
{
    // Initialization code. Don't use any Avalonia APIs or SynchronizationContext-reliant code
    // before StartWithClassicDesktopLifetime runs: Avalonia isn't initialized yet. Starting the
    // generic host first is fine, because hosted services never depend on the UI thread.
    [STAThread]
    public static int Main(string[] args)
    {
        using IHost host = DesktopHost.Create(args);
        try
        {
            host.Start();
        }
        catch (Exception exception)
        {
            // Such as a library database that can't be opened: said in a window, rather than the app closing unseen.
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program).FullName!);
            LogCantStart(logger, exception);
            string message = exception is LibraryDatabaseUnavailableException
                ? exception.Message
                : string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.StartupFailed), exception.Message);
            MessageDialogContent problem = new(Strings.StartupProblemTitle, message, Strings.DialogClose, null);
            Configure(AppBuilder.Configure(() => new App(problem))).StartWithClassicDesktopLifetime(args);
            return 1;
        }

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
