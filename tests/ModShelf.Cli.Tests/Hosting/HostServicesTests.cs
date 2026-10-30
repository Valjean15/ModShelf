using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Hosting;
using ModShelf.Platform;
using ModShelf.Platform.Links;

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
    public void ByDefaultTheSystemStartsTheDesktopAppBesideThisOneAsItIs()
    {
        SchemeHandlerOptions options = HostServices.SchemeHandlerOptionsFrom(new ConfigurationBuilder().Build());

        Assert.Equal(PlatformServiceCollectionExtensions.DefaultDesktopApp, options.DesktopApp);
        Assert.Empty(options.Arguments);
    }
}
