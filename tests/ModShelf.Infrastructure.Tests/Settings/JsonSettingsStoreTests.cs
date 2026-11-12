using System.Collections.Immutable;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Settings;
using ModShelf.Sources.Abstractions.Search;
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
        Assert.Empty(settings.Games);
        Assert.Null(settings.OldVersions);
        Assert.Equal(FilePath, Store().Location);
    }

    [Fact]
    public async Task ReadsBackWhatItSaved()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        FolderPattern byId = FolderPattern.Parse("{source}/{game}/{modid}/{file}");
        FolderPattern flat = FolderPattern.Parse("{mod}/{file}");
        await Store().SaveAsync(Settings(root, byId, ("nexus/skyrimspecialedition", flat)), Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal((root, byId), (settings.LibraryRoot, settings.LibraryFolders));
        Assert.Equal(flat, settings.FoldersFor(Nexus("SkyrimSpecialEdition")));
        Assert.Equal(byId, settings.FoldersFor(Nexus("stardewvalley")));
    }

    [Fact]
    public async Task TheFileIsTheDocumentedJson()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        await Store().SaveAsync(Settings(root, FolderPattern.Default, ("nexus/stardewvalley", FolderPattern.Parse("{mod}/{file}"))), Cancellation);

        string expected = $$"""
            {
              "library": {
                "root": {{System.Text.Json.JsonSerializer.Serialize(root)}},
                "folders": "{source}/{game}/{mod} ({modid})/{file}"
              },
              "games": {
                "nexus/stardewvalley": {
                  "folders": "{mod}/{file}"
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings(), (await File.ReadAllTextAsync(FilePath, Cancellation)).TrimEnd().ReplaceLineEndings());
    }

    [Fact]
    public async Task SettingsBackToTheirDefaultsLeaveTheFile()
    {
        await Store().SaveAsync(Settings(Path.Combine(_folder.Path, "Mods"), FolderPattern.Default) with { QuotaWarnBelow = 5 }, Cancellation);

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

    [Fact]
    public async Task KeepsThePreviewsThePicturesAndTheDetails()
    {
        // A screen this version doesn't know, and a setting of a newer one, stay.
        await WriteAsync("""{ "previews": { "history": "cards" }, "pictures": { "format": "webp" } }""");
        AppSettings settings = new()
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty
                .Add(PreviewScreen.Library, PreviewLevel.Cards)
                .Add(PreviewScreen.Downloads, PreviewLevel.Thumbnails),
            PicturesMegabytes = 1024,
            AdultPictures = AdultPictures.Hidden,
            DetailsMaxAge = TimeSpan.FromDays(7),
        };

        await Store().SaveAsync(settings, Cancellation);

        AppSettings loaded = await Store().LoadAsync(Cancellation);
        Assert.Equal(settings.Previews.OrderBy(entry => entry.Key), loaded.Previews.OrderBy(entry => entry.Key));
        Assert.Equal((1024, AdultPictures.Hidden, TimeSpan.FromDays(7)), (loaded.PicturesMegabytes!.Value, loaded.AdultPictures!.Value, loaded.DetailsMaxAge!.Value));
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        foreach (string written in new[] { "\"library\": \"cards\"", "\"downloads\": \"thumbnails\"", "\"history\": \"cards\"", "\"maxSize\": 1024", "\"adult\": \"hidden\"", "\"format\": \"webp\"", "\"maxAge\": \"7d\"" })
        {
            Assert.Contains(written, file, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task KeepsAgentsAccessAndItsPortAndTheirDefaultsLeaveTheFile()
    {
        await WriteAsync("""{ "agents": { "later": true } }""");

        await Store().SaveAsync(new AppSettings { AgentsEnabled = true, AgentsPort = 50000 }, Cancellation);
        AppSettings loaded = await Store().LoadAsync(Cancellation);
        string allowed = await File.ReadAllTextAsync(FilePath, Cancellation);
        await Store().SaveAsync(new AppSettings(), Cancellation);
        string reset = await File.ReadAllTextAsync(FilePath, Cancellation);

        Assert.Equal((true, 50000), (loaded.AgentsEnabled, loaded.AgentsPort));
        Assert.Contains("\"enabled\": true", allowed, StringComparison.Ordinal);
        Assert.Contains("\"port\": 50000", allowed, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("port", reset, StringComparison.Ordinal);
        Assert.Contains("\"later\": true", reset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheBrowsersDownloadsFolderAndItsDefaultLeavesTheFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Browser downloads");
        await WriteAsync("""{ "downloads": { "later": true } }""");

        await Store().SaveAsync(new AppSettings { BrowserDownloadsFolder = folder }, Cancellation);
        AppSettings loaded = await Store().LoadAsync(Cancellation);
        string chosen = await File.ReadAllTextAsync(FilePath, Cancellation);
        await Store().SaveAsync(new AppSettings(), Cancellation);
        string reset = await File.ReadAllTextAsync(FilePath, Cancellation);
        await WriteAsync("{}");
        await Store().SaveAsync(new AppSettings(), Cancellation);
        string none = await File.ReadAllTextAsync(FilePath, Cancellation);

        Assert.Equal(folder, loaded.BrowserDownloadsFolder);
        Assert.Contains("\"browserFolder\"", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("browserFolder", reset, StringComparison.Ordinal);
        Assert.Contains("\"later\": true", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("downloads", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheQuotaWarningAndItsDefaultLeavesTheFile()
    {
        await WriteAsync("""{ "quota": { "warnBelow": 10, "later": true } }""");

        await Store().SaveAsync(new AppSettings { QuotaWarnBelow = 0 }, Cancellation);
        AppSettings loaded = await Store().LoadAsync(Cancellation);
        string off = await File.ReadAllTextAsync(FilePath, Cancellation);
        await Store().SaveAsync(new AppSettings(), Cancellation);
        string reset = await File.ReadAllTextAsync(FilePath, Cancellation);

        Assert.Equal(0, loaded.QuotaWarnBelow);
        Assert.Contains("\"warnBelow\": 0", off, StringComparison.Ordinal);
        Assert.DoesNotContain("warnBelow", reset, StringComparison.Ordinal);
        Assert.Contains("\"later\": true", reset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePreviewsPicturesAndDetailsDefaultsLeaveTheFile()
    {
        await Store().SaveAsync(
            new AppSettings
            {
                Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Browse, PreviewLevel.Off),
                PicturesMegabytes = 256,
                AdultPictures = AdultPictures.Shown,
                DetailsMaxAge = TimeSpan.FromHours(6),
            },
            Cancellation);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        AppSettings loaded = await Store().LoadAsync(Cancellation);
        Assert.Empty(loaded.Previews);
        Assert.Equal(((int?)null, (AdultPictures?)null, (TimeSpan?)null), (loaded.PicturesMegabytes, loaded.AdultPictures, loaded.DetailsMaxAge));
        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Fact]
    public async Task ReadsPreviewsAndAdultPicturesWrittenByHandInAnyCase()
    {
        await WriteAsync("""{ "previews": { "Updates": " THUMBNAILS " }, "pictures": { "adult": "Blurred" } }""");

        AppSettings loaded = await Store().LoadAsync(Cancellation);

        Assert.Equal(PreviewLevel.Thumbnails, loaded.PreviewFor(PreviewScreen.Updates));
        Assert.Equal(AdultPictures.Blurred, loaded.AdultPictures);
    }

    [Theory]
    [InlineData("""{ "previews": { "library": "big" } }""", "previews.library must be off, thumbnails or cards.")]
    [InlineData("""{ "previews": { "browse": 2 } }""", "previews.browse must be text.")]
    [InlineData("""{ "pictures": { "maxSize": 63 } }""", "pictures.maxSize must be a number of megabytes from 64 to 8192.")]
    [InlineData("""{ "pictures": { "maxSize": "512" } }""", "pictures.maxSize must be a number of megabytes from 64 to 8192.")]
    [InlineData("""{ "pictures": { "adult": "maybe" } }""", "pictures.adult must be hidden, blurred or shown.")]
    [InlineData("""{ "details": { "maxAge": "a year" } }""", "'a year' isn't an interval.")]
    [InlineData("""{ "quota": { "warnBelow": 91 } }""", "quota.warnBelow must be a percent from 0 to 90.")]
    [InlineData("""{ "quota": { "warnBelow": "10%" } }""", "quota.warnBelow must be a percent from 0 to 90.")]
    [InlineData("""{ "agents": { "enabled": "yes" } }""", "agents.enabled must be true or false.")]
    [InlineData("""{ "agents": { "port": 80 } }""", "agents.port must be a port from 1024 to 65535.")]
    [InlineData("""{ "agents": { "port": 65536 } }""", "agents.port must be a port from 1024 to 65535.")]
    [InlineData("""{ "agents": { "port": "47823" } }""", "agents.port must be a port from 1024 to 65535.")]
    [InlineData("""{ "downloads": { "browserFolder": 3 } }""", "downloads.browserFolder must be text.")]
    [InlineData("""{ "downloads": { "browserFolder": "Downloads" } }""", "downloads.browserFolder must be a full path, not 'Downloads'.")]
    public async Task SaysWhenAPreviewPictureOrDetailsSettingIsWrong(string content, string message)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: {message}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheGamesPickedForInstalledGamesAndNoneLeavesTheFile()
    {
        await WriteAsync("""{ "installedGames": { "steam/1": { "nexus": "old" } } }""");
        AppSettings picked = new()
        {
            InstalledMatches = ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>>.Empty
                .Add("steam/489830", new Dictionary<SourceId, string?> { [new SourceId("nexus")] = "skyrimspecialedition", [new SourceId("mod-io")] = null })
                .Add("steam/413150", new Dictionary<SourceId, string?> { [new SourceId("gamebanana")] = "6745" }),
        };

        await Store().SaveAsync(picked, Cancellation);
        AppSettings loaded = await Store().LoadAsync(Cancellation);
        string saved = await File.ReadAllTextAsync(FilePath, Cancellation);
        await Store().SaveAsync(new AppSettings(), Cancellation);
        string none = await File.ReadAllTextAsync(FilePath, Cancellation);

        Assert.Equal(["steam/413150", "steam/489830"], loaded.InstalledMatches.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("6745", loaded.InstalledMatches["steam/413150"][new SourceId("gamebanana")]);
        Assert.Equal("skyrimspecialedition", loaded.InstalledMatches["steam/489830"][new SourceId("nexus")]);
        Assert.True(loaded.InstalledMatches["steam/489830"].TryGetValue(new SourceId("mod-io"), out string? modIo) && modIo is null);
        Assert.DoesNotContain("steam/1\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"mod-io\": null", saved, StringComparison.Ordinal);
        Assert.True(saved.IndexOf("steam/413150", StringComparison.Ordinal) < saved.IndexOf("steam/489830", StringComparison.Ordinal));
        Assert.DoesNotContain("installedGames", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstalledGameWithNoSiteIsntKept()
    {
        await WriteAsync("""{ "installedGames": { "steam/1": {} } }""");

        AppSettings loaded = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(new AppSettings { InstalledMatches = ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>>.Empty.Add("steam/2", new Dictionary<SourceId, string?>()) }, Cancellation);

        Assert.Empty(loaded.InstalledMatches);
        Assert.DoesNotContain("installedGames", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "installedGames": [] }""", "installedGames must hold the installed games by their store and ID.")]
    [InlineData("""{ "installedGames": { "489830": { "nexus": "skyrim" } } }""", "installedGames.489830 must name a store and a game, such as steam/489830, and hold its games by site.")]
    [InlineData("""{ "installedGames": { "steam/": { "nexus": "skyrim" } } }""", "installedGames.steam/ must name a store and a game")]
    [InlineData("""{ "installedGames": { "/489830": { "nexus": "skyrim" } } }""", "installedGames./489830 must name a store and a game")]
    [InlineData("""{ "installedGames": { "steam/489830": "nexus/skyrim" } }""", "installedGames.steam/489830 must name a store and a game")]
    [InlineData("""{ "installedGames": { "steam/489830": { "nexus": 3 } } }""", "installedGames.steam/489830.nexus must be a game's key, or null for none.")]
    [InlineData("""{ "installedGames": { "steam/489830": { "nexus": " " } } }""", "installedGames.steam/489830.nexus must be a game's key, or null for none.")]
    [InlineData("""{ "installedGames": { "steam/489830": { "Nexus Mods": "skyrim" } } }""", "'Nexus Mods' is not a valid source ID.")]
    public async Task SaysWhenAGamePickedForAnInstalledGameIsWrong(string content, string message)
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

    [Fact]
    public async Task KeepsWhetherTheFirstRunSetupIsDone()
    {
        await Store().SaveAsync(new AppSettings { SetupDone = true }, Cancellation);

        Assert.True((await Store().LoadAsync(Cancellation)).SetupDone);
        Assert.Contains("\"setup\": {", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Null((await Store().LoadAsync(Cancellation)).SetupDone);
        Assert.Equal("{}", (await File.ReadAllTextAsync(FilePath, Cancellation)).Trim());
    }

    [Fact]
    public async Task KeepsWhatElseTheSetupSectionHolds()
    {
        await WriteAsync("""{ "setup": { "doneIn": "0.9.0" } }""");

        await Store().SaveAsync(new AppSettings { SetupDone = true }, Cancellation);
        await Store().SaveAsync(new AppSettings(), Cancellation);

        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"doneIn\": \"0.9.0\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("\"done\":", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsWhetherToCheckForANewerModShelf()
    {
        await WriteAsync("""{ "app": { "channel": "beta" } }""");

        await Store().SaveAsync(new AppSettings { CheckForAppUpdates = false }, Cancellation);

        Assert.False((await Store().LoadAsync(Cancellation)).CheckForAppUpdates);
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"checkForUpdates\": false", file, StringComparison.Ordinal);
        Assert.Contains("\"channel\": \"beta\"", file, StringComparison.Ordinal);

        await Store().SaveAsync(new AppSettings(), Cancellation);

        Assert.Null((await Store().LoadAsync(Cancellation)).CheckForAppUpdates);
        Assert.DoesNotContain("checkForUpdates", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenCheckForUpdatesIsntTrueOrFalse()
    {
        await WriteAsync("""{ "app": { "checkForUpdates": 0 } }""");

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: app.checkForUpdates must be true or false.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenSetupDoneIsntTrueOrFalse()
    {
        await WriteAsync("""{ "setup": { "done": "yes" } }""");

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: setup.done must be true or false.", refused.Message, StringComparison.Ordinal);
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
    public async Task KeepsEachGamesOwnSettings()
    {
        GameSettings stardew = new()
        {
            Folders = FolderPattern.Parse("{mod}/{file}"),
            OldVersions = OldVersionRule.KeepLatest(3),
            CheckUpdates = false,
            UpdateInterval = TimeSpan.FromDays(1),
            Notify = true,
            Browse = new BrowseDefaults { Sort = SearchSort.UniqueDownloads, Direction = SearchDirection.Ascending, UpdatedWithin = TimeSpan.FromDays(30), PageSize = 100 },
            Hidden = true,
        };
        GameSettings skyrim = new() { OldVersions = OldVersionRule.Archive, Browse = new BrowseDefaults { UpdatedWithin = TimeSpan.FromDays(1) } };
        await Store().SaveAsync(new AppSettings { Games = Games(("nexus/StardewValley", stardew), ("nexus/skyrimspecialedition", skyrim)) }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal((stardew, skyrim), (settings.GameFor(Nexus("stardewvalley")), settings.GameFor(Nexus("SkyrimSpecialEdition"))));
        string expected = """
            {
              "games": {
                "nexus/skyrimspecialedition": {
                  "oldVersions": "archive",
                  "browse": {
                    "window": "1d"
                  }
                },
                "nexus/StardewValley": {
                  "folders": "{mod}/{file}",
                  "oldVersions": 3,
                  "checkUpdates": false,
                  "updateInterval": "1d",
                  "notify": true,
                  "browse": {
                    "sort": "uniquedownloads",
                    "direction": "ascending",
                    "window": "30d",
                    "pageSize": 100
                  },
                  "hidden": true
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings(), (await File.ReadAllTextAsync(FilePath, Cancellation)).TrimEnd().ReplaceLineEndings());
    }

    [Fact]
    public async Task KeepsAGamesOwnFolderFirst()
    {
        string folder = Path.Combine(_folder.Path, "Stardew mods");
        GameSettings stardew = new() { Root = folder, Folders = FolderPattern.Parse("{mod}/{file}") };
        await Store().SaveAsync(new AppSettings { Games = Games(("nexus/stardewvalley", stardew)) }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(stardew, settings.GameFor(Nexus("stardewvalley")));
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.True(
            file.IndexOf("\"root\"", StringComparison.Ordinal) < file.IndexOf("\"folders\"", StringComparison.Ordinal),
            "A game's folder comes before its pattern.");
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(folder), file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsAGamesOwnPreviews()
    {
        GameSettings stardew = new() { Notify = false, Previews = PreviewLevel.Cards, Hidden = true };
        GameSettings skyrim = new() { Previews = PreviewLevel.Off };
        await Store().SaveAsync(new AppSettings { Games = Games(("nexus/stardewvalley", stardew), ("nexus/skyrimspecialedition", skyrim)) }, Cancellation);

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal((stardew, skyrim), (settings.GameFor(Nexus("stardewvalley")), settings.GameFor(Nexus("skyrimspecialedition"))));
        Assert.Equal(PreviewLevel.Cards, settings.PreviewFor(PreviewScreen.Browse, Nexus("stardewvalley")));
        string expected = """
            {
              "games": {
                "nexus/skyrimspecialedition": {
                  "previews": "off"
                },
                "nexus/stardewvalley": {
                  "notify": false,
                  "previews": "cards",
                  "hidden": true
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings(), (await File.ReadAllTextAsync(FilePath, Cancellation)).TrimEnd().ReplaceLineEndings());
    }

    [Fact]
    public async Task ReadsAGamesPreviewsWrittenByHandAndForgetsThemWhenGone()
    {
        await WriteAsync("""{ "games": { "fallout4": { "previews": " Thumbnails ", "colour": "teal" } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(settings with { Games = Games(("nexus/fallout4", new GameSettings { Notify = true })) }, Cancellation);

        Assert.Equal(new GameSettings { Previews = PreviewLevel.Thumbnails }, settings.GameFor(Nexus("fallout4")));
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.DoesNotContain("previews", file, StringComparison.Ordinal);
        Assert.Contains("\"colour\": \"teal\"", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsGamesFoldersAsTheyWereKeptAndMovesThemWhenSaving()
    {
        await WriteAsync("""
            {
              "library": { "folders": "{modid}/{file}", "gameFolders": { "skyrimspecialedition": "{mod}/{file}", "stardewvalley": "{file}" } },
              "games": { "STARDEWVALLEY": { "folders": "{modid}/{file}", "hidden": true } }
            }
            """);

        AppSettings settings = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(settings, Cancellation);

        Assert.Equal(new GameSettings { Folders = FolderPattern.Parse("{mod}/{file}") }, settings.GameFor(Nexus("skyrimspecialedition")));
        Assert.Equal(new GameSettings { Folders = FolderPattern.Parse("{modid}/{file}"), Hidden = true }, settings.GameFor(Nexus("stardewvalley")));
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.DoesNotContain("gameFolders", file, StringComparison.Ordinal);
        Assert.Contains("\"nexus/skyrimspecialedition\"", file, StringComparison.Ordinal);
        Assert.Contains("\"nexus/STARDEWVALLEY\"", file, StringComparison.Ordinal);
        Assert.Equal(settings.Games.Values, (await Store().LoadAsync(Cancellation)).Games.Values);
    }

    [Fact]
    public async Task AGamesOldFoldersStayWhenItsNewSettingsHaveNone()
    {
        await WriteAsync("""{ "library": { "gameFolders": { "fallout4": "{mod}/{file}" } }, "games": { "fallout4": { "notify": false } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(new GameSettings { Folders = FolderPattern.Parse("{mod}/{file}"), Notify = false }, settings.GameFor(Nexus("fallout4")));
    }

    [Fact]
    public async Task KeepsWhatElseAGameHoldsAndForgetsAGameWithNothingLeft()
    {
        await WriteAsync("""{ "games": { "fallout4": { "hidden": true, "colour": "teal" }, "stardewvalley": { "notify": false }, "skyrimspecialedition": { "browse": { "pageSize": 20, "view": "grid" } } } }""");

        await Store().SaveAsync(new AppSettings(), Cancellation);

        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"colour\": \"teal\"", file, StringComparison.Ordinal);
        Assert.Contains("\"view\": \"grid\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", file, StringComparison.Ordinal);
        Assert.DoesNotContain("stardewvalley", file, StringComparison.Ordinal);
        Assert.DoesNotContain("pageSize", file, StringComparison.Ordinal);
        Assert.Contains("\"nexus/fallout4\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("\"fallout4\"", file, StringComparison.Ordinal);
        Assert.Empty((await Store().LoadAsync(Cancellation)).Games);
    }

    [Fact]
    public async Task ReadsAGamesSettingsWrittenByHandInAnyCase()
    {
        await WriteAsync("""{ "games": { "fallout4": { "browse": { "sort": "Updated", "direction": " DESCENDING ", "window": "7D" }, "oldVersions": "all", "folders": "" } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(
            new GameSettings
            {
                OldVersions = OldVersionRule.KeepAll,
                Browse = new BrowseDefaults { Sort = SearchSort.Updated, Direction = SearchDirection.Descending, UpdatedWithin = TimeSpan.FromDays(7) },
            },
            settings.GameFor(Nexus("fallout4")));
    }

    [Fact]
    public async Task AGamesNewSettingsNameItAsTheyDo()
    {
        await WriteAsync("""{ "library": { "gameFolders": { "stardewvalley": "{mod}/{file}" } }, "games": { "STARDEWVALLEY": { "notify": false } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(Nexus("STARDEWVALLEY"), Assert.Single(settings.Games).Key);
    }

    [Fact]
    public async Task ReadsEachSitesGameApart()
    {
        await WriteAsync("""{ "games": { "nexus/stardewvalley": { "hidden": true }, "example/stardewvalley": { "notify": false } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);

        Assert.Equal(2, settings.Games.Count);
        Assert.Equal(new GameSettings { Hidden = true }, settings.GameFor(Nexus("stardewvalley")));
        Assert.Equal(new GameSettings { Notify = false }, settings.GameFor(new GameRef(new SourceId("example"), "stardewvalley")));
    }

    [Fact]
    public async Task AGameNamedByItsKeyAloneIsNexusModsAndIsNamedByItsSiteWhenSaved()
    {
        // As 0.9.x wrote them, when only Nexus Mods had games.
        await WriteAsync("""{ "games": { "stardewvalley": { "notify": false, "colour": "teal" } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(settings, Cancellation);

        Assert.Equal(Nexus("stardewvalley"), Assert.Single(settings.Games).Key);
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"nexus/stardewvalley\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("\"stardewvalley\"", file, StringComparison.Ordinal);
        Assert.Contains("\"colour\": \"teal\"", file, StringComparison.Ordinal);
        Assert.Equal(settings.Games, (await Store().LoadAsync(Cancellation)).Games);
    }

    [Fact]
    public async Task AGameNamedByItsSiteComesBeforeTheSameGameNamedByItsKeyAlone()
    {
        await WriteAsync("""{ "games": { "nexus/stardewvalley": { "hidden": true, "colour": "teal" }, "STARDEWVALLEY": { "notify": false, "colour": "red" } } }""");

        AppSettings settings = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(settings, Cancellation);

        (GameRef game, GameSettings own) = Assert.Single(settings.Games);
        Assert.Equal((Nexus("stardewvalley"), new GameSettings { Hidden = true }), (game, own));
        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"colour\": \"teal\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("red", file, StringComparison.Ordinal);
        Assert.DoesNotContain("STARDEWVALLEY", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseLeftAsBrowsesOwnIsLeftOut()
    {
        await WriteAsync("""{ "games": { "fallout4": { "hidden": true, "browse": { } } } }""");

        Assert.Equal(new GameSettings { Hidden = true }, (await Store().LoadAsync(Cancellation)).GameFor(Nexus("fallout4")));
    }

    [Fact]
    public async Task AGameWithNoSettingIsLeftOut()
    {
        await WriteAsync("""{ "games": { "fallout4": { }, "stardewvalley": { "browse": { } } } }""");

        Assert.Empty((await Store().LoadAsync(Cancellation)).Games);
    }

    [Theory]
    [InlineData("""{ "games": [] }""", "games must hold each game's settings.")]
    [InlineData("""{ "games": { "fallout4": true } }""", "games.fallout4 must hold the game's settings.")]
    [InlineData("""{ "games": { "fallout4": { "folders": 3 } } }""", "games.fallout4.folders must be text.")]
    [InlineData("""{ "games": { "fallout4": { "folders": "{mod}" } } }""", "")]
    [InlineData("""{ "games": { "fallout4": { "oldVersions": true } } }""", "games.fallout4.oldVersions must be all, archive, or a number of versions to keep.")]
    [InlineData("""{ "games": { "fallout4": { "checkUpdates": "no" } } }""", "games.fallout4.checkUpdates must be true or false.")]
    [InlineData("""{ "games": { "fallout4": { "updateInterval": "1m" } } }""", "'1m' isn't an interval.")]
    [InlineData("""{ "games": { "fallout4": { "notify": 1 } } }""", "games.fallout4.notify must be true or false.")]
    [InlineData("""{ "games": { "fallout4": { "hidden": "yes" } } }""", "games.fallout4.hidden must be true or false.")]
    [InlineData("""{ "games": { "fallout4": { "previews": "big" } } }""", "games.fallout4.previews must be off, thumbnails or cards.")]
    [InlineData("""{ "games": { "fallout4": { "previews": 2 } } }""", "games.fallout4.previews must be text.")]
    [InlineData("""{ "games": { "fallout4": { "root": "Fallout mods" } } }""", "games.fallout4.root must be a full path, not 'Fallout mods'.")]
    [InlineData("""{ "games": { "fallout4": { "root": 4 } } }""", "games.fallout4.root must be text.")]
    [InlineData("""{ "games": { "fallout4": { "browse": 3 } } }""", "games.fallout4.browse must hold how Browse shows the game's mods.")]
    [InlineData("""{ "games": { "fallout4": { "browse": { "sort": "best" } } } }""", "games.fallout4.browse.sort must be one of relevance, ")]
    [InlineData("""{ "games": { "fallout4": { "browse": { "direction": "up" } } } }""", "games.fallout4.browse.direction must be ascending or descending.")]
    [InlineData("""{ "games": { "fallout4": { "browse": { "window": "2d" } } } }""", "games.fallout4.browse.window must be 1d, 7d or 30d.")]
    [InlineData("""{ "games": { "fallout4": { "browse": { "pageSize": 30 } } } }""", "games.fallout4.browse.pageSize must be 20, 50 or 100.")]
    [InlineData("""{ "games": { "fallout4": { "browse": { "pageSize": "50" } } } }""", "games.fallout4.browse.pageSize must be 20, 50 or 100.")]
    [InlineData("""{ "library": { "gameFolders": [] } }""", "library.gameFolders must hold a folder pattern for each game.")]
    [InlineData("""{ "games": { "nexus/": { "hidden": true } } }""", "games.nexus/ must name a game by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("""{ "games": { "/fallout4": { "hidden": true } } }""", "games./fallout4 must name a game by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("""{ "games": { "Nexus Mods/fallout4": { "hidden": true } } }""", "games.Nexus Mods/fallout4 must name a game by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("""{ "games": { " ": { "hidden": true } } }""", "games.  must name a game by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("""{ "library": { "gameFolders": { "nexus/": "{mod}/{file}" } } }""", "library.gameFolders.nexus/ must name a game by its site and its key there, such as nexus/stardewvalley.")]
    public async Task SaysWhenAGamesSettingIsWrong(string content, string message)
    {
        await WriteAsync(content);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => Store().LoadAsync(Cancellation));

        Assert.StartsWith($"The settings in {FilePath} can't be read: {message}", refused.Message, StringComparison.Ordinal);
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
        Games = games.ToImmutableDictionary(game => Game(game.Game), game => new GameSettings { Folders = game.Pattern }, AppSettings.GameComparer),
    };

    /// <summary>
    /// Games' own settings, by the games as text, such as nexus/stardewvalley.
    /// </summary>
    private static ImmutableDictionary<GameRef, GameSettings> Games(params (string Game, GameSettings Settings)[] games)
        => games.ToImmutableDictionary(game => Game(game.Game), game => game.Settings, AppSettings.GameComparer);

    private static GameRef Game(string text) => GameRef.TryParse(text, out GameRef? game) ? game : throw new ArgumentException($"'{text}' isn't a game.", nameof(text));

    private static GameRef Nexus(string key) => new(new SourceId("nexus"), key);

    private JsonSettingsStore Store() => new(FilePath);

    private async Task WriteAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
    }
}
