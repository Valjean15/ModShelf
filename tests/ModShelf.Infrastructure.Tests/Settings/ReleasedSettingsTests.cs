using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Settings;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Settings;

/// <summary>
/// 1.0.0 reads the <c>settings.json</c> every version released before it wrote, 0.9.0 to 0.9.5, with every setting it
/// had, and saves it in its own shape, losing none.
/// </summary>
public sealed class ReleasedSettingsTests : IDisposable
{
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");

    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder.Path, "settings.json");

    private string Root => Path.Combine(_folder.Path, "Mods");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ReadsWhat090To092WroteAndSavesItAs100Does()
    {
        // As 0.9.0, 0.9.1 and 0.9.2 documented it: a game's folders in the library's section, by its key alone.
        await WriteAsync($$"""
            {
              "library": {
                "root": {{System.Text.Json.JsonSerializer.Serialize(Root)}},
                "folders": "{source}/{game}/{mod} ({modid})/{file}",
                "gameFolders": { "skyrimspecialedition": "{source}/{game}/{mod}/{file}" },
                "oldVersions": 2
              },
              "updates": { "interval": "6h", "notify": false },
              "appearance": { "theme": "dark" },
              "window": { "closeToTray": true },
              "setup": { "done": true },
              "app": { "checkForUpdates": false }
            }
            """);

        AppSettings read = await new JsonSettingsStore(FilePath).LoadAsync(Cancellation);
        AssertCommon(read);
        Assert.Equal("{source}/{game}/{mod}/{file}", read.GameFor(Skyrim).Folders?.Text);

        await new JsonSettingsStore(FilePath).SaveAsync(read, Cancellation);
        string saved = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.DoesNotContain("gameFolders", saved, StringComparison.Ordinal);
        Assert.Contains("\"nexus/skyrimspecialedition\"", saved, StringComparison.Ordinal);
        AssertSame(read, await new JsonSettingsStore(FilePath).LoadAsync(Cancellation));
    }

    [Fact]
    public async Task ReadsWhat093To095WroteAndSavesItAs100Does()
    {
        // As 0.9.3, 0.9.4 and 0.9.5 documented it: games by their key alone, with their own settings, and pictures.
        await WriteAsync($$"""
            {
              "library": {
                "root": {{System.Text.Json.JsonSerializer.Serialize(Root)}},
                "folders": "{source}/{game}/{mod} ({modid})/{file}",
                "oldVersions": 2
              },
              "games": {
                "skyrimspecialedition": {
                  "folders": "{source}/{game}/{mod}/{file}",
                  "oldVersions": "archive",
                  "checkUpdates": true,
                  "updateInterval": "1d",
                  "notify": false,
                  "browse": { "sort": "updated", "direction": "descending", "window": "7d", "pageSize": 50 },
                  "hidden": true
                }
              },
              "updates": { "interval": "6h", "notify": false },
              "appearance": { "theme": "dark" },
              "window": { "closeToTray": true },
              "setup": { "done": true },
              "app": { "checkForUpdates": false },
              "previews": { "library": "cards", "updates": "thumbnails" },
              "pictures": { "maxSize": 1024, "adult": "hidden" },
              "details": { "maxAge": "7d" }
            }
            """);

        AppSettings read = await new JsonSettingsStore(FilePath).LoadAsync(Cancellation);
        AssertCommon(read);
        GameSettings skyrim = read.GameFor(Skyrim);
        Assert.Equal(("{source}/{game}/{mod}/{file}", OldVersionRule.Archive, true, TimeSpan.FromDays(1), false, true), (skyrim.Folders?.Text, skyrim.OldVersions, skyrim.CheckUpdates, skyrim.UpdateInterval, skyrim.Notify, skyrim.Hidden));
        Assert.Equal((PreviewLevel.Cards, PreviewLevel.Thumbnails), (read.PreviewFor(PreviewScreen.Library), read.PreviewFor(PreviewScreen.Updates)));
        Assert.Equal((1024, AdultPictures.Hidden, TimeSpan.FromDays(7)), (read.PicturesMegabytes!.Value, read.AdultPictures!.Value, read.DetailsMaxAge!.Value));
        Assert.Null(read.QuotaWarnBelow);

        await new JsonSettingsStore(FilePath).SaveAsync(read, Cancellation);
        string saved = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"nexus/skyrimspecialedition\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("quota", saved, StringComparison.Ordinal);
        AssertSame(read, await new JsonSettingsStore(FilePath).LoadAsync(Cancellation));
    }

    /// <summary>
    /// The same settings, the games' and the screens' compared by what they hold.
    /// </summary>
    private static void AssertSame(AppSettings expected, AppSettings actual)
    {
        Assert.Equal(expected with { Games = actual.Games, Previews = actual.Previews }, actual);
        Assert.Equal(expected.Games.Count, actual.Games.Count);
        Assert.Equal(expected.GameFor(Skyrim), actual.GameFor(Skyrim));
        Assert.Equal(expected.Previews.OrderBy(entry => entry.Key), actual.Previews.OrderBy(entry => entry.Key));
    }

    private void AssertCommon(AppSettings read)
    {
        Assert.Equal((Root, "{source}/{game}/{mod} ({modid})/{file}", OldVersionRule.KeepLatest(2)), (read.LibraryRoot, read.LibraryFolders?.Text, read.OldVersions));
        Assert.Equal((TimeSpan.FromHours(6), false, AppTheme.Dark, true, true, false), (read.UpdateInterval!.Value, read.NotifyOfUpdates!.Value, read.Theme!.Value, read.CloseToTray!.Value, read.SetupDone!.Value, read.CheckForAppUpdates!.Value));
    }

    private async Task WriteAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
    }
}
