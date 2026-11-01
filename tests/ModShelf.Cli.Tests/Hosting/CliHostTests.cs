using Microsoft.Extensions.Configuration;

using ModShelf.Cli.Hosting;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Hosting;

/// <summary>
/// The CLI's settings file: in an installed ModShelf, beside the desktop app's appsettings.json, whose settings it
/// takes precedence over, while the environment's still take precedence over it.
/// </summary>
public sealed class CliHostTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TheClisSettingsComeAfterTheDesktopAppsAndBeforeTheEnvironments()
    {
        File.WriteAllText(Path.Combine(_folder.Path, "appsettings.json"), """{ "Logging": { "LogLevel": { "Default": "Information", "Microsoft": "Warning" } } }""");
        File.WriteAllText(Path.Combine(_folder.Path, CliHost.SettingsFile), """{ "Logging": { "LogLevel": { "Default": "Warning", "ModShelf": "Error" } } }""");
        ConfigurationManager configuration = new();
        configuration.SetBasePath(_folder.Path);
        configuration.AddJsonFile("appsettings.json", optional: true);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:ModShelf"] = "Debug" });

        CliHost.AddCliSettings(configuration, _folder.Path);

        Assert.Equal("Warning", configuration["Logging:LogLevel:Default"]);
        Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft"]);
        Assert.Equal("Debug", configuration["Logging:LogLevel:ModShelf"]);
    }

    [Fact]
    public void WithoutTheDesktopAppsTheClisAreReadAllTheSame()
    {
        File.WriteAllText(Path.Combine(_folder.Path, CliHost.SettingsFile), """{ "Logging": { "LogLevel": { "Default": "Warning" } } }""");
        ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ModShelf:DatabasePath"] = "library.db" });

        CliHost.AddCliSettings(configuration, _folder.Path);

        Assert.Equal("Warning", configuration["Logging:LogLevel:Default"]);
        Assert.Equal("library.db", configuration["ModShelf:DatabasePath"]);
    }

    [Fact]
    public void TheClisOwnSettingsFileIsBesideIt()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, CliHost.SettingsFile)));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")), "The CLI ships the desktop app's settings file's name.");
    }
}
