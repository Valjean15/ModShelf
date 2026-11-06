using System.Globalization;
using System.Text;

using Avalonia;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Releases;
using ModShelf.Application.State;
using ModShelf.Desktop.Hosting;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Infrastructure.Logging;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Platform;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.FirstRun;

using Velopack;

namespace ModShelf.Desktop;

internal static partial class Program
{
    // Initialization code. Don't use any Avalonia APIs or SynchronizationContext-reliant code
    // before StartWithClassicDesktopLifetime runs: Avalonia isn't initialized yet. Starting the
    // generic host first is fine, because hosted services never depend on the UI thread.
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack first, before anything else runs: the installer, an update and the uninstaller start the app with
        // Velopack's own arguments, which it handles, then ends the process. An update downloaded but not yet applied
        // waits for the user's restart, rather than a second launch, such as a link's, applying it under the app running.
        VelopackApp velopack = VelopackApp.Build()
            .SetAppUserModelId(PlatformServiceCollectionExtensions.WindowsAppId)
            .SetAutoApplyOnStartup(false);
        if (OperatingSystem.IsWindows())
        {
            // Only Windows has an uninstaller: the other systems' apps are removed as files.
            velopack.OnBeforeUninstallFastCallback(_ => CleanUpBeforeUninstall());
        }

        velopack.Run();

        using IHost host = DesktopHost.Create(args);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program).FullName!);

        // One app per library: a second launch shows the one running instead, before this one starts any work.
        ISingleInstance instance = host.Services.GetRequiredService<ISingleInstance>();
        try
        {
            if (!instance.TryClaim(args))
            {
                LogHandedOver(logger);
                return 0;
            }
        }
        catch (TimeoutException exception)
        {
            LogNotAnswering(logger, exception);
            ShowProblem(new MessageDialogContent(Strings.StartupProblemTitle, Strings.StartupNotAnswering, Strings.DialogClose, null), args);
            return 1;
        }

        try
        {
            host.Start();
        }
        catch (Exception exception)
        {
            // Such as a library database that can't be opened: said in a window, rather than the app closing unseen.
            LogCantStart(logger, exception);
            string message = exception is LibraryDatabaseUnavailableException
                ? exception.Message
                : string.Format(
                    CultureInfo.CurrentCulture,
                    CompositeFormat.Parse(Strings.StartupFailed),
                    exception.Message,
                    host.Services.GetRequiredService<LogFolder>().Path);
            ShowProblem(new MessageDialogContent(Strings.StartupProblemTitle, message, Strings.DialogClose, null), args);
            return 1;
        }

        // The window comes back as it was left. Read before Avalonia starts, so the window opens there rather than moving
        // once shown: Main stays synchronous for Avalonia's STA thread, with no SynchronizationContext yet to block, and
        // reading the state never fails.
        UiState remembered = host.Services.GetRequiredService<IUiState>().GetAsync(CancellationToken.None).GetAwaiter().GetResult();
        MainWindowViewModel? window = null;
        try
        {
            return CreateAppBuilder(host.Services, () => window = host.Services.GetRequiredService<MainWindowViewModel>(), instance, remembered, args)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Avalonia leaves its SynchronizationContext on this thread after its dispatcher has
            // stopped. Clear it, or a continuation in a hosted service's StopAsync (or in an async
            // Dispose) would be posted to the dead dispatcher and the process would never exit.
            SynchronizationContext.SetSynchronizationContext(null);

            // The window's place, told as the window closed, is saved off the UI thread: given a moment to finish, as
            // Main stays synchronous.
            if (window is not null)
            {
                Task.WhenAny(window.Remembering, Task.Delay(TimeSpan.FromSeconds(2))).GetAwaiter().GetResult();
            }

            // Avalonia needs an STA thread on Windows, so Main stays synchronous and stops the
            // host through its synchronous helpers: WaitForShutdown waits for StopApplication.
            host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            host.WaitForShutdown();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => Configure(AppBuilder.Configure<App>());

    private static AppBuilder CreateAppBuilder(
        IServiceProvider services, Func<MainWindowViewModel> createMainWindowViewModel, ISingleInstance instance, UiState remembered, string[] args)
        => Configure(AppBuilder.Configure(
            () => new App(
                createMainWindowViewModel,
                () => services.GetRequiredService<TrayViewModel>(),
                () => services.GetRequiredService<FirstRunViewModel>(),
                instance,
                remembered,
                args)));

    /// <summary>
    /// As the Windows uninstaller starts: gives the sites' links back and removes what ModShelf registered for itself.
    /// Velopack ends the process after this, or after 30 seconds.
    /// </summary>
    private static void CleanUpBeforeUninstall()
    {
        using IHost host = DesktopHost.Create([]);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        // Velopack's hooks are synchronous, and this runs before Avalonia, with no SynchronizationContext to block.
        host.Services.GetRequiredService<IAppUninstall>().CleanUpAsync(timeout.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Shows why ModShelf can't start, in a window of its own, until it's closed.
    /// </summary>
    private static void ShowProblem(MessageDialogContent problem, string[] args)
        => Configure(AppBuilder.Configure(() => new App(problem))).StartWithClassicDesktopLifetime(args);

    private static AppBuilder Configure(AppBuilder builder)
        => builder
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
