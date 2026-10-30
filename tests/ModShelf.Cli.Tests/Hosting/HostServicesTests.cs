using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Hosting;

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
}
