using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// The CLI with the fake sources plugged in, run in this process. Its library database, settings, library and secret
/// files sit in a folder of its own, and secrets in the system's keyring under a scope of its own, so the user's own
/// ModShelf data is never touched. Each command builds its own host, as each run of the real CLI does, so the app
/// restarts between commands.
/// </summary>
internal sealed class InProcessCli : IAsyncDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeAccountSite _site;
    private readonly ScriptedBrowser _browser;
    private readonly Dictionary<string, string?> _settings;
    private readonly IReadOnlyList<IModSource> _sources;

    /// <param name="settings">More configuration, as a user would set it, such as a Nexus Mods client ID.</param>
    /// <param name="sources">More sources, such as a site that asks for handoffs.</param>
    public InProcessCli(
        FakeAccountSite site,
        ScriptedBrowser browser,
        IReadOnlyDictionary<string, string?>? settings = null,
        IReadOnlyList<IModSource>? sources = null)
    {
        _site = site;
        _browser = browser;
        _sources = sources ?? [];
        _settings = new Dictionary<string, string?>
        {
            [CliHost.DatabasePathKey] = Path.Combine(_folder.Path, "library.db"),
            [CliHost.SecretsFolderKey] = Path.Combine(_folder.Path, "Secrets"),
            [CliHost.SecretScopeKey] = string.Create(CultureInfo.InvariantCulture, $"ModShelf.Tests.{Guid.NewGuid():N}"),

            // Everything is logged, so the tests can check that no credential is.
            ["Logging:LogLevel:Default"] = "Trace",
        };
        foreach ((string key, string? value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// The folder holding everything the app keeps on disk here.
    /// </summary>
    public string Folder => _folder.Path;

    /// <summary>
    /// The user's Downloads folder as the app sees it here, so the library's default root is in the test's folder too.
    /// </summary>
    public string Downloads => Path.Combine(_folder.Path, "Downloads");

    public async Task<CliResult> RunAsync(string[] arguments, CancellationToken cancellationToken, string input = "")
    {
        using StringReader typed = new(input);
        await using CliRun run = Start(arguments, typed);
        return await run.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public CliRun Start(string[] arguments, TextReader input) => new(arguments, input, BuildHost);

    /// <summary>
    /// Starts the app as the CLI builds it, without a command, runs <paramref name="work"/>, and stops the app again:
    /// a restart in which a source uses its API.
    /// </summary>
    public async Task<T> InAppAsync<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken)
    {
        using IHost app = BuildHost(runDownloads: false);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work(app.Services).ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Unlinks the fake sources, which removes what the tests stored in the system's keyring, then deletes the folder.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await InAppAsync(
                async services =>
                {
                    IAccountService accounts = services.GetRequiredService<IAccountService>();
                    await accounts.UnlinkAsync(FakeOAuthSource.SourceId, CancellationToken.None).ConfigureAwait(false);
                    await accounts.UnlinkAsync(FakeApiKeySource.SourceId, CancellationToken.None).ConfigureAwait(false);
                    return true;
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        // Pooled connections keep the database open, which stops Windows from deleting it.
        SqliteConnection.ClearAllPools();
        Logs.Dispose();
        _folder.Dispose();
    }

    private IHost BuildHost(bool runDownloads) => CliHost.Build(runDownloads, _settings, services =>
    {
        // The logs go to the test, not to the test process's console.
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider>(Logs);

        services.AddSingleton<IBrowserLauncher>(_browser);
        services.AddSingleton<IUserFolders>(new TestUserFolders(Downloads));
        services.AddSingleton<IModSource>(provider => new FakeOAuthSource(
            _site,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IModSource>(provider => new FakeApiKeySource(_site, provider.GetRequiredService<IHttpClientFactory>()));
        foreach (IModSource source in _sources)
        {
            services.AddSingleton(source);
        }
    });
}
