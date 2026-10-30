using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Releases.Ports;
using ModShelf.Hosting;
using ModShelf.Platform;
using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Hosting;

/// <summary>
/// The services and files both hosts have, from their configuration.
/// </summary>
public sealed class HostServicesTests
{
    [Fact]
    public void ADatabasePathGivenRelativeIsTakenFromTheCurrentFolder()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [HostServices.DatabasePathKey] = Path.Combine("data", "library.db") })
            .Build();

        string folder = new ServiceCollection().AddModShelf(configuration, "cli");

        Assert.Equal(Path.GetFullPath("data"), folder);
    }

    [Fact]
    public void TheSystemStartsTheDesktopAppOnThisLibraryWithItsSecrets()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ModShelf checklist");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HostServices.DatabasePathKey] = Path.Combine(folder, "library.db"),
                [HostServices.SecretsFolderKey] = Path.Combine(folder, "Secrets"),
                [HostServices.SecretScopeKey] = "ModShelf.Checklist",
                [HostServices.DesktopAppKey] = Path.Combine("bin", "ModShelf.Desktop"),
            })
            .Build();

        SchemeHandlerOptions options = HostServices.SchemeHandlerOptionsFrom(configuration);

        Assert.Equal(Path.GetFullPath(Path.Combine("bin", "ModShelf.Desktop")), options.DesktopApp);
        Assert.Equal(
            [
                $"--ModShelf:DatabasePath={Path.Combine(folder, "library.db")}",
                $"--ModShelf:SecretsFolder={Path.Combine(folder, "Secrets")}",
                "--ModShelf:SecretScope=ModShelf.Checklist",
            ],
            options.Arguments);
    }

    [Fact]
    public async Task OnlyAnInstalledAppLooksForANewerModShelf()
    {
        using TempFolder folder = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HostServices.DatabasePathKey] = Path.Combine(folder.Path, "library.db"),
                [HostServices.UpdateFeedKey] = Path.Combine(folder.Path, "feed"),
            })
            .Build();
        ServiceCollection services = new();
        services.AddModShelf(configuration, "cli");
        await using ServiceProvider provider = services.BuildServiceProvider();

        IAppReleases releases = provider.GetRequiredService<IAppReleases>();

        Assert.False(releases.IsInstalled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ByDefaultTheSystemStartsTheDesktopAppBesideThisOneAsItIs()
    {
        SchemeHandlerOptions options = HostServices.SchemeHandlerOptionsFrom(new ConfigurationBuilder().Build());

        Assert.Equal(PlatformServiceCollectionExtensions.DefaultDesktopApp, options.DesktopApp);
        Assert.Empty(options.Arguments);
    }
}
