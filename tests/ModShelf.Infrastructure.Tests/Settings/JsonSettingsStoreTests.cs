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
        await WriteAsync("""{ "updates": { "interval": "6h" }, "library": { "keep": 3, "root": "/old" } }""");

        await Store().SaveAsync(new AppSettings { LibraryFolders = FolderPattern.Default }, Cancellation);

        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"interval\": \"6h\"", file, StringComparison.Ordinal);
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
