using System.Collections.Immutable;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Library;
using ModShelf.Infrastructure.Settings;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder.Path, "App Data", "settings.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task WithoutAFileEverySettingIsItsDefault()
    {
        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Null(settings.LibraryRoot);
        Assert.Null(settings.LibraryFolders);
        Assert.Empty(settings.GameFolders);
        Assert.Null(settings.OldVersions);
        Assert.Equal(FilePath, Store().Location);
    }

    [Fact]
    public async Task ReadsBackWhatItSaved()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        FolderPattern byId = FolderPattern.Parse("{source}/{game}/{modid}/{file}");
        FolderPattern flat = FolderPattern.Parse("{mod}/{file}");
        await Store().SaveAsync(Settings(root, byId, ("skyrimspecialedition", flat)), Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal((root, byId), (settings.LibraryRoot, settings.LibraryFolders));
        Assert.Equal(flat, settings.FoldersFor("SkyrimSpecialEdition"));
        Assert.Equal(byId, settings.FoldersFor("stardewvalley"));
    }

    [Fact]
    public async Task TheFileIsTheDocumentedJson()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        await Store().SaveAsync(Settings(root, FolderPattern.Default, ("stardewvalley", FolderPattern.Parse("{mod}/{file}"))), Cancellation);

        string expected = $$"""
            {
              "library": {
                "root": {{System.Text.Json.JsonSerializer.Serialize(root)}},
                "folders": "{source}/{game}/{mod} ({modid})/{file}",
                "gameFolders": {
                  "stardewvalley": "{mod}/{file}"
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings(), (await File.ReadAllTextAsync(FilePath, Cancellation)).TrimEnd().ReplaceLineEndings());
    }

    [Fact]
    public async Task SettingsBackToTheirDefaultsLeaveTheFile()
    {
        await Store().SaveAsync(Settings(Path.Combine(_folder.Path, "Mods"), FolderPattern.Default), Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Fact]
    public async Task KeepsWhatElseTheFileHolds()
    {
        // Settings of a newer version, beside these and in their sections.
        await WriteAsync("""{ "window": { "width": 900 }, "appearance": { "accent": "teal" }, "library": { "keep": 3, "root": "/old" } }""");

        await Store().SaveAsync(new AppSettings { LibraryFolders = FolderPattern.Default }, Cancellation);

        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"width\": 900", file, StringComparison.Ordinal);
        Assert.Contains("\"accent\": \"teal\"", file, StringComparison.Ordinal);
        Assert.Contains("\"keep\": 3", file, StringComparison.Ordinal);
        Assert.DoesNotContain("/old", file, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task ReadsAFileEditedByHand()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        await WriteAsync($$"""
            {
              // Where my mods go.
              "library": {
                "root": {{System.Text.Json.JsonSerializer.Serialize(root)}},
                "folders": "",
              },
            }
            """);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(root, settings.LibraryRoot);
        Assert.Null(settings.LibraryFolders);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "library": { "root": "Mods" } }""")]
    [InlineData("""{ "library": { "root": 3 } }""")]
    [InlineData("""{ "library": { "folders": "{mod}" } }""")]
    [InlineData("""{ "library": { "gameFolders": { "skyrimspecialedition": 7 } } }""")]
    [InlineData("""{ "library": { "gameFolders": { "skyrimspecialedition": null } } }""")]
    public async Task SaysWhichFileItCantRead(string content)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read:", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhichFileItCantOpen()
    {
        Directory.CreateDirectory(FilePath);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read:", refused.Message, StringComparison.Ordinal);
        Assert.True(refused.InnerException is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task SaysWhichFileItCantSave()
    {
        // The folder the file goes in is a file.
        string folder = Path.GetDirectoryName(FilePath)!;
        await File.WriteAllTextAsync(folder, "not a folder", Cancellation);

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Store().SaveAsync(new AppSettings { NotifyOfUpdates = false }, Cancellation));

        Assert.StartsWith($"The settings couldn't be saved to {FilePath}:", refused.Message, StringComparison.Ordinal);
        Assert.Equal([folder], Directory.GetFileSystemEntries(_folder.Path));
    }

    [Fact]
    public async Task AFileThatCantBeReplacedLeavesNoTemporaryFileBehind()
    {
        // A folder where the file goes can't be replaced by it.
        string folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(Path.Combine(FilePath, "taken"));

        Exception? refused = await Record.ExceptionAsync(() => JsonSettingsStore.ReplaceAsync(FilePath, "{}", Cancellation));

        Assert.True(refused is IOException or UnauthorizedAccessException, refused?.ToString());
        Assert.Equal([FilePath], Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public async Task EachSaveWritesItsOwnTemporaryFile()
    {
        // Left by a save that was cut short, such as one of an earlier version.
        await WriteAsync("{}");
        Directory.CreateDirectory(FilePath + ".tmp");

        await Store().SaveAsync(new AppSettings { NotifyOfUpdates = false }, Cancellation);

        Assert.False((await Store().LoadAsync(Cancellation)).NotifyOfUpdates);
    }

    [Theory]
    [InlineData("all", "\"all\"")]
    [InlineData("archive", "\"archive\"")]
    [InlineData("3", "3")]
    public async Task KeepsTheRuleForOldVersions(string rule, string json)
    {
        await Store().SaveAsync(new AppSettings { OldVersions = OldVersionRule.Parse(rule) }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(OldVersionRule.Parse(rule), settings.OldVersions);
        Assert.Contains($"\"oldVersions\": {json}", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsARuleForOldVersionsWrittenByHand()
    {
        await WriteAsync("""{ "library": { "oldVersions": " Archive " } }""");
        OldVersionRule? archive = (await Store().LoadAsync(Cancellation)).OldVersions;
        await WriteAsync("""{ "library": { "oldVersions": "4" } }""");
        OldVersionRule? four = (await Store().LoadAsync(Cancellation)).OldVersions;

        Assert.Equal((OldVersionRule.Archive, OldVersionRule.KeepLatest(4)), (archive, four));
    }

    [Theory]
    [InlineData("""{ "library": { "oldVersions": 0 } }""")]
    [InlineData("""{ "library": { "oldVersions": 2.5 } }""")]
    [InlineData("""{ "library": { "oldVersions": "some" } }""")]
    [InlineData("""{ "library": { "oldVersions": true } }""")]
    public async Task SaysWhenTheRuleForOldVersionsIsWrong(string content)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read:", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaultRuleLeavesTheFile()
    {
        await Store().SaveAsync(new AppSettings { OldVersions = OldVersionRule.Archive }, Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Fact]
    public async Task KeepsHowOftenToCheckForUpdates()
    {
        await Store().SaveAsync(new AppSettings { UpdateInterval = TimeSpan.FromMinutes(90) }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(TimeSpan.FromMinutes(90), settings.UpdateInterval);
        Assert.Contains("\"updates\": {", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
        Assert.Contains("\"interval\": \"90m\"", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaultIntervalLeavesTheFile()
    {
        await Store().SaveAsync(new AppSettings { UpdateInterval = TimeSpan.FromHours(1) }, Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Theory]
    [InlineData("""{ "updates": { "interval": "1w" } }""")]
    [InlineData("""{ "updates": { "interval": "5m" } }""")]
    [InlineData("""{ "updates": { "interval": 6 } }""")]
    public async Task SaysWhenTheIntervalIsWrong(string content)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read:", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepsWhetherToNotifyOfUpdates(bool notify)
    {
        await Store().SaveAsync(new AppSettings { UpdateInterval = TimeSpan.FromHours(1), NotifyOfUpdates = notify }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal((notify, TimeSpan.FromHours(1)), (settings.NotifyOfUpdates, settings.UpdateInterval));
        Assert.Contains($"\"notify\": {(notify ? "true" : "false")}", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotifyingByDefaultLeavesTheFile()
    {
        await Store().SaveAsync(new AppSettings { NotifyOfUpdates = false }, Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Null((await Store().LoadAsync(Cancellation)).NotifyOfUpdates);
        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Theory]
    [InlineData("""{ "updates": { "notify": "no" } }""")]
    [InlineData("""{ "updates": { "notify": 0 } }""")]
    public async Task SaysWhenNotifyIsntTrueOrFalse(string content)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.Contains("updates.notify must be true or false.", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AppTheme.System, "system")]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.Dark, "dark")]
    public async Task KeepsTheTheme(AppTheme theme, string name)
    {
        await WriteAsync("""{ "appearance": { "accent": "teal" } }""");

        await Store().SaveAsync(new AppSettings { Theme = theme }, Cancellation);

        Assert.Equal(theme, (await Store().LoadAsync(Cancellation)).Theme);
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"appearance\": {", file, StringComparison.Ordinal);
        Assert.Contains($"\"theme\": \"{name}\"", file, StringComparison.Ordinal);
        Assert.Contains("\"accent\": \"teal\"", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaultThemeLeavesTheFile()
    {
        await Store().SaveAsync(new AppSettings { Theme = AppTheme.Dark }, Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Null((await Store().LoadAsync(Cancellation)).Theme);
        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Theory]
    [InlineData("\" Dark \"", AppTheme.Dark)]
    [InlineData("\"LIGHT\"", AppTheme.Light)]
    [InlineData("\"\"", null)]
    public async Task ReadsAThemeWrittenByHand(string json, AppTheme? theme)
    {
        await WriteAsync($$"""{ "appearance": { "theme": {{json}} } }""");

        Assert.Equal(theme, (await Store().LoadAsync(Cancellation)).Theme);
    }

    [Theory]
    [InlineData("""{ "appearance": { "theme": "blue" } }""", "appearance.theme must be system, light or dark.")]
    [InlineData("""{ "appearance": { "theme": "1" } }""", "appearance.theme must be system, light or dark.")]
    [InlineData("""{ "appearance": { "theme": 2 } }""", "appearance.theme must be text.")]
    public async Task SaysWhenTheThemeIsWrong(string content, string message)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: {message}", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepsWhetherClosingTheWindowKeepsModShelfInTheTray(bool closeToTray)
    {
        await WriteAsync("""{ "window": { "width": 900 } }""");

        await Store().SaveAsync(new AppSettings { CloseToTray = closeToTray }, Cancellation);

        Assert.Equal(closeToTray, (await Store().LoadAsync(Cancellation)).CloseToTray);
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"window\": {", file, StringComparison.Ordinal);
        Assert.Contains($"\"closeToTray\": {(closeToTray ? "true" : "false")}", file, StringComparison.Ordinal);
        Assert.Contains("\"width\": 900", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskingWhenTheWindowClosesLeavesTheFile()
    {
        await Store().SaveAsync(new AppSettings { CloseToTray = true }, Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Null((await Store().LoadAsync(Cancellation)).CloseToTray);
        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Theory]
    [InlineData("""{ "window": { "closeToTray": "yes" } }""")]
    [InlineData("""{ "window": { "closeToTray": 1 } }""")]
    public async Task SaysWhenCloseToTrayIsntTrueOrFalse(string content)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: window.closeToTray must be true or false.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesARelativePath()
    {
        Assert.Throws<ArgumentException>(() => new JsonSettingsStore(Path.Combine("App Data", "settings.json")));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSettingsFile("settings.json"));
    }

    [Fact]
    public async Task IsRegisteredAsTheSettingsStore()
    {
        ServiceCollection services = new();
        services.AddSettingsFile(FilePath);
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(FilePath, provider.GetRequiredService<ISettingsStore>().Location);
    }

    private static AppSettings Settings(string root, FolderPattern folders, params (string Game, FolderPattern Pattern)[] games) => new()
    {
        LibraryRoot = root,
        LibraryFolders = folders,
        GameFolders = games.ToImmutableDictionary(game => game.Game, game => game.Pattern, StringComparer.OrdinalIgnoreCase),
    };

    private JsonSettingsStore Store() => new(FilePath);

    private async Task WriteAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
    }
}
