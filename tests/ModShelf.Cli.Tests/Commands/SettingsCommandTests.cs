using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>settings show</c>, <c>set</c> and <c>reset</c>, with the settings file next to the test's database.
/// </summary>
public sealed class SettingsCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public SettingsCommandTests()
    {
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string SettingsFile => Path.Combine(_cli.Folder, "settings.json");

    private string Mods => Path.Combine(_cli.Folder, "Mods");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ShowsTheDefaultsAndWhereTheSettingsAreKept()
    {
        CliResult shown = await RunAsync("show");

        Assert.Equal(Done, shown.ExitCode);
        Assert.Contains($"Settings are kept in {SettingsFile}.", shown.Output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_cli.Downloads, "ModShelf") + " (default)", Value(shown, "library.root"));
        Assert.Equal("{source}/{game}/{mod} ({modid})/{file} (default)", Value(shown, "library.folders"));
        Assert.Equal("2 (default)", Value(shown, "library.oldVersions"));
        Assert.Equal("6h (default)", Value(shown, "updates.interval"));
        Assert.Equal("true (default)", Value(shown, "updates.notify"));
        Assert.Contains("To change a setting: settings set <setting> <value>", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsTheLibrarysRoot()
    {
        CliResult set = await RunAsync("set", "library.root", Mods + Path.DirectorySeparatorChar);

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal($"library.root is now {Mods}.", set.Output.Trim());
        Assert.Equal(Mods, Value(await RunAsync("show"), "library.root"));
        Assert.Contains("\"root\"", await File.ReadAllTextAsync(SettingsFile, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsFolderPatternsForEveryGameAndForOne()
    {
        CliResult every = await RunAsync("set", "library.folders", "{source}/{game}/{modid}/{file}");
        CliResult one = await RunAsync("set", "Library.Folders.SkyrimSpecialEdition", "{mod}/{file}");

        Assert.Equal("library.folders is now {source}/{game}/{modid}/{file}.", every.Output.Trim());
        Assert.Equal("library.folders.SkyrimSpecialEdition is now {mod}/{file}.", one.Output.Trim());
        CliResult shown = await RunAsync("show");
        Assert.Equal("{source}/{game}/{modid}/{file}", Value(shown, "library.folders"));
        Assert.Equal("{mod}/{file}", Value(shown, "library.folders.SkyrimSpecialEdition"));
    }

    [Fact]
    public async Task ResetsSettingsToTheirDefaults()
    {
        await RunAsync("set", "library.root", Mods);
        await RunAsync("set", "library.folders", "{modid}/{file}");
        await RunAsync("set", "library.folders.skyrimspecialedition", "{mod}/{file}");

        CliResult root = await RunAsync("reset", "library.root");
        CliResult game = await RunAsync("reset", "library.folders.skyrimspecialedition");
        CliResult folders = await RunAsync("reset", "library.folders");

        Assert.Equal($"library.root is back to its default, {Path.Combine(_cli.Downloads, "ModShelf")}.", root.Output.Trim());
        Assert.Equal("library.folders.skyrimspecialedition is gone, so skyrimspecialedition's files go by library.folders again.", game.Output.Trim());
        Assert.Equal("library.folders is back to its default, {source}/{game}/{mod} ({modid})/{file}.", folders.Output.Trim());
        CliResult shown = await RunAsync("show");
        Assert.EndsWith("(default)", Value(shown, "library.root"), StringComparison.Ordinal);
        Assert.DoesNotContain("skyrimspecialedition", shown.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("all", "library.oldVersions is now all: every version of a file stays.")]
    [InlineData("Archive", "library.oldVersions is now archive: when a newer version of a file is downloaded, the old ones move to an _archive folder.")]
    [InlineData("1", "library.oldVersions is now 1: when a newer version of a file is downloaded, the old ones are deleted.")]
    [InlineData("3", "library.oldVersions is now 3: when a newer version of a file is downloaded, the newest 3 stay and older ones are deleted.")]
    public async Task SetsWhatHappensToOldVersions(string value, string said)
    {
        CliResult set = await RunAsync("set", "Library.OldVersions", value);

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal(said, set.Output.Trim());
        Assert.Equal(value.ToLowerInvariant(), Value(await RunAsync("show"), "library.oldVersions"));
    }

    [Fact]
    public async Task ResetsWhatHappensToOldVersions()
    {
        await RunAsync("set", "library.oldVersions", "archive");

        CliResult reset = await RunAsync("reset", "library.oldVersions");

        Assert.Equal(
            "library.oldVersions is back to its default, 2: when a newer version of a file is downloaded, the newest 2 stay and older ones are deleted.",
            reset.Output.Trim());
        Assert.Equal("2 (default)", Value(await RunAsync("show"), "library.oldVersions"));
    }

    [Theory]
    [InlineData("set", "library.root", "Mods", "isn't a full path")]
    [InlineData("set", "library.folders", "{mod}", "The last part must be exactly {file}.")]
    [InlineData("set", "library.folders.skyrimspecialedition", "{name}/{file}", "{name} isn't a folder's part.")]
    [InlineData("set", "library.colour", "blue", "There's no setting called library.colour. Settings: library.root, library.folders, library.folders.<game>, library.oldVersions, updates.interval, updates.notify.")]
    [InlineData("reset", "library.folders.", null, "There's no setting called library.folders.")]
    [InlineData("set", "library.oldVersions", "0", "'0' isn't a rule for old versions. Use all, archive, or how many versions to keep, from 1 to 99, such as 2.")]
    public async Task SaysWhatsWrongWithASetting(string command, string name, string? value, string message)
    {
        CliResult refused = await RunAsync([command, name, .. value is null ? Array.Empty<string>() : [value]]);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", refused.Errors, StringComparison.Ordinal);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task KeepsTheRootWhileTheLibraryHasFiles()
    {
        await _cli.InAppAsync(
            services =>
            {
                ModRef mod = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
                LibraryFile file = new(
                    new ModFileRef(mod, "160380"), "SMAPI 4.5.2", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, "nexus/stardewvalley/SMAPI (2400)/SMAPI.zip", 1, DateTimeOffset.UnixEpoch);
                return services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                    mod, _ => LibraryMod.Watch(mod, "SMAPI", null, null, DateTimeOffset.UnixEpoch).WithFile(file), Cancellation);
            },
            Cancellation);

        CliResult refused = await RunAsync("set", "library.root", Mods);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Contains($"The library has files in {Path.Combine(_cli.Downloads, "ModShelf")}.", refused.Errors, StringComparison.Ordinal);
        Assert.EndsWith($"To move it there: library move \"{Mods}\"", refused.Errors.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSettingsFileCantBeRead()
    {
        await File.WriteAllTextAsync(SettingsFile, "{ not json", Cancellation);

        CliResult shown = await RunAsync("show");
        CliResult set = await RunAsync("set", "library.folders", "{mod}/{file}");

        Assert.Equal((Failed, Failed), (shown.ExitCode, set.ExitCode));
        Assert.Contains($"The settings in {SettingsFile} can't be read:", shown.Errors, StringComparison.Ordinal);
        Assert.Contains("Fix the file, or delete it to start again.", set.Errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value <c>settings show</c> gives a setting.
    /// </summary>
    private static string Value(CliResult shown, string setting)
    {
        string row = Assert.Single(shown.Output.ReplaceLineEndings("\n").Split('\n'), line => line.StartsWith(setting + " ", StringComparison.Ordinal));
        return row[setting.Length..].Trim();
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(["settings", .. arguments], Cancellation);
}
