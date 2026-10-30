using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Accounts;
using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Updates;
using ModShelf.Application.Watchlist;

namespace ModShelf.Application;

/// <summary>
/// Registers Application's services. The hosts call these from their composition roots.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the download engine: the <see cref="ISourceRegistry"/>, the <see cref="DownloadScheduler"/> as the
    /// <see cref="IDownloadService"/>, and <see cref="ILinkDownloads"/>. Nothing downloads until
    /// <see cref="AddBackgroundDownloads"/> is also called. Needs the queue lock and the file storage from Infrastructure.
    /// </summary>
    public static IServiceCollection AddDownloadEngine(this IServiceCollection services, DownloadSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options ?? new DownloadSchedulerOptions());
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.AddSingleton<DownloadScheduler>();
        services.AddSingleton<IDownloadService>(provider => provider.GetRequiredService<DownloadScheduler>());
        services.AddSingleton<ILinkDownloads, LinkDownloads>();

        return services;
    }

    /// <summary>
    /// Registers account linking: one <see cref="AccountService"/> as both the <see cref="IAccountService"/> and the
    /// <see cref="ICredentialProvider"/>. Needs the ports from Platform (<see cref="ISecretStore"/>,
    /// <see cref="IOAuthRedirectListener"/>, <see cref="IBrowserLauncher"/>) and Infrastructure
    /// (<see cref="ILinkedAccountRepository"/>).
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<IAccountService>(provider => provider.GetRequiredService<AccountService>());
        services.AddSingleton<ICredentialProvider>(provider => provider.GetRequiredService<AccountService>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IModLookup"/>, which looks mods up through the sources' catalogs.
    /// </summary>
    public static IServiceCollection AddModLookup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<IModLookup, ModLookup>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IModLibrary"/>, the library of watched and downloaded mods. Needs the settings
    /// (<see cref="AddSettings"/>), and Infrastructure's <see cref="Library.Ports.ILibraryRepository"/>, which
    /// <c>AddLibraryFiles()</c> registers, and <see cref="Library.Ports.ILibraryStorage"/>.
    /// </summary>
    public static IServiceCollection AddLibrary(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<LibraryChanges>();
        services.TryAddSingleton<IModLibrary, ModLibrary>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ILibraryDownloads"/>, which downloads files into the library and records them there. Needs
    /// the download engine (<see cref="AddDownloadEngine"/>) and the settings (<see cref="AddSettings"/>), and
    /// Infrastructure's <see cref="Library.Ports.ILibraryRepository"/> and <see cref="Library.Ports.ILibraryStorage"/>.
    /// </summary>
    public static IServiceCollection AddLibraryDownloads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<LibraryChanges>();

        // One instance, whose lock the mover holds too.
        services.TryAddSingleton<LibraryDownloads>();
        services.TryAddSingleton<ILibraryDownloads>(provider => provider.GetRequiredService<LibraryDownloads>());
        services.TryAddSingleton<ILibraryHold>(provider => provider.GetRequiredService<LibraryDownloads>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="ILibraryMover"/>, which moves the library to another root, with the library downloads
    /// (<see cref="AddLibraryDownloads"/>), which hold still while it does; it needs what they need.
    /// </summary>
    public static IServiceCollection AddLibraryMover(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLibraryDownloads();
        services.TryAddSingleton<ILibraryMover, LibraryMover>();

        return services;
    }

    /// <summary>
    /// Records finished downloads into the library while the host runs, with a hosted <see cref="LibraryRecorder"/>.
    /// A host that runs downloads (<see cref="AddBackgroundDownloads"/>) adds this too, with
    /// <see cref="AddLibraryDownloads"/>.
    /// </summary>
    public static IServiceCollection AddLibraryRecording(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<LibraryRecorder>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISettingsService"/>. Needs Infrastructure's <see cref="Settings.Ports.ISettingsStore"/> and
    /// <see cref="Library.Ports.ILibraryRepository"/>, and Platform's <see cref="Settings.Ports.IUserFolders"/>.
    /// </summary>
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SettingsService>();
        services.TryAddSingleton<ISettingsService>(provider => provider.GetRequiredService<SettingsService>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IUpdateChecker"/>, which checks the watched mods for updates. Needs Infrastructure's
    /// <see cref="Library.Ports.ILibraryRepository"/>.
    /// </summary>
    public static IServiceCollection AddUpdateChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<LibraryChanges>();
        services.TryAddSingleton<IUpdateChecker, UpdateChecker>();

        return services;
    }

    /// <summary>
    /// Checks the watched mods for updates in the background while the host runs, every
    /// <see cref="AppSettings.UpdateInterval"/>, with a hosted <see cref="UpdateCheckService"/>, which shows a notice of
    /// new updates. For the desktop app; the CLI checks only when asked. Needs <see cref="AddUpdateChecks"/>,
    /// <see cref="AddSettings"/>, and Platform's notices.
    /// </summary>
    public static IServiceCollection AddBackgroundUpdateChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<UpdateCheckService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IModUpdates"/>, which asks the sources' update feeds which mods changed lately.
    /// </summary>
    public static IServiceCollection AddModUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<IModUpdates, ModUpdates>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IWatchlists"/>, which reads and changes the mods the linked accounts follow on their sites.
    /// Needs Infrastructure's <see cref="Library.Ports.ILibraryRepository"/>.
    /// </summary>
    public static IServiceCollection AddWatchlists(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.TryAddSingleton<IWatchlists, Watchlists>();

        return services;
    }

    /// <summary>
    /// Runs the <see cref="DownloadScheduler"/> as a hosted service, so queued jobs download while the host runs.
    /// A host that only reads the queue leaves this out.
    /// </summary>
    public static IServiceCollection AddBackgroundDownloads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService(provider => provider.GetRequiredService<DownloadScheduler>());

        return services;
    }
}
