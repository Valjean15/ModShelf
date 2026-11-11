using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.State;
using ModShelf.Application.State.Ports;
using ModShelf.Infrastructure.State;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.State;

/// <summary>
/// The desktop app's state in ui-state.json: read back as saved, a file that can't be read forgotten and replaced, a
/// value that isn't what it should be left out, and what else the file holds kept.
/// </summary>
public sealed class JsonUiStateStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<JsonUiStateStore> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder.Path, "App Data", "ui-state.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task WithoutAFileNothingIsRemembered()
    {
        UiState state = await Store().LoadAsync(Cancellation);

        Assert.Same(UiState.None, state);
        Assert.Equal(FilePath, Store().Location);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task ReadsBackWhatItSavedAsTheDocumentedJson()
    {
        UiState saved = new()
        {
            Window = new WindowPlacement(-1600, 40, 1280.5, 800, maximized: true),
            MenuCollapsed = true,
            Screen = "browse",
            BrowseSearches = ["skyui", "armour"],
            BrowseSite = "degmods",
            QuickSearches = ["dragon", "old versions"],
        };

        await Store().SaveAsync(saved, Cancellation);

        Assert.Equal(saved, await Store().LoadAsync(Cancellation));
        string expected = """
            {
              "window": {
                "x": -1600,
                "y": 40,
                "width": 1280.5,
                "height": 800,
                "maximized": true
              },
              "menuCollapsed": true,
              "screen": "browse",
              "browseSearches": [
                "skyui",
                "armour"
              ],
              "browseSite": "degmods",
              "quickSearches": [
                "dragon",
                "old versions"
              ]
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings(), (await File.ReadAllTextAsync(FilePath, Cancellation)).TrimEnd().ReplaceLineEndings());
    }

    [Fact]
    public async Task WhatsNotRememberedIsLeftOutAndWhatElseTheFileHoldsStays()
    {
        await WriteAsync("""
            {
              // Written by a later version.
              "window": { "x": 1, "y": 2, "width": 3, "height": 4, "monitor": "DISPLAY2" },
              "menuCollapsed": false,
              "screen": "library",
              "browseSearches": [ "old" ],
              "browseSite": "nexus",
              "libraryOrder": "name",
            }
            """);

        await Store().SaveAsync(new UiState { Window = new WindowPlacement(10, 20, 900, 600, maximized: false) }, Cancellation);

        string file = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"libraryOrder\": \"name\"", file, StringComparison.Ordinal);
        Assert.Contains("\"monitor\": \"DISPLAY2\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("menuCollapsed", file, StringComparison.Ordinal);
        Assert.DoesNotContain("screen", file, StringComparison.Ordinal);
        Assert.DoesNotContain("browseSearches", file, StringComparison.Ordinal);
        Assert.DoesNotContain("browseSite", file, StringComparison.Ordinal);
        Assert.Equal(new UiState { Window = new WindowPlacement(10, 20, 900, 600, maximized: false) }, await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task AWindowNoLongerRememberedLeavesTheFile()
    {
        await Store().SaveAsync(new UiState { Window = new WindowPlacement(10, 20, 900, 600, maximized: false), Screen = "updates" }, Cancellation);

        await Store().SaveAsync(new UiState { Screen = "updates" }, Cancellation);

        Assert.DoesNotContain("window", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"browse\"")]
    [InlineData("")]
    public async Task AFileThatCantBeReadIsForgottenAndReplacedWhenSaved(string content)
    {
        await WriteAsync(content);

        UiState state = await Store().LoadAsync(Cancellation);
        await Store().SaveAsync(new UiState { Screen = "downloads" }, Cancellation);

        Assert.Same(UiState.None, state);
        Assert.Contains(_logger.Entries, entry => entry.Contains(FilePath, StringComparison.Ordinal) && entry.Contains("can't be read", StringComparison.Ordinal));
        Assert.Equal(new UiState { Screen = "downloads" }, await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task AFolderInTheFilesPlaceIsForgottenAndSavingSaysWhy()
    {
        Directory.CreateDirectory(FilePath);

        UiState state = await Store().LoadAsync(Cancellation);
        IOException refused = await Assert.ThrowsAsync<IOException>(() => Store().SaveAsync(new UiState { Screen = "browse" }, Cancellation));

        Assert.Same(UiState.None, state);
        Assert.StartsWith($"The UI state couldn't be saved to {FilePath}: ", refused.Message, StringComparison.Ordinal);
        Assert.NotEmpty(_logger.Entries);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Theory]
    [InlineData("""{ "window": { "x": 1, "y": 2, "width": 0, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": 1, "y": 2, "width": 800, "height": -1 } }""")]
    [InlineData("""{ "window": { "x": 1.5, "y": 2, "width": 800, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": 1, "y": 3000000000, "width": 800, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": "1", "y": 2, "width": 800, "height": 600 } }""")]
    [InlineData("""{ "window": { "y": 2, "width": 800, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": 1, "width": 800, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": 1, "y": 2, "height": 600 } }""")]
    [InlineData("""{ "window": { "x": 1, "y": 2, "width": 800 } }""")]
    [InlineData("""{ "window": [ 1, 2, 800, 600 ] }""")]
    public async Task AWindowThatIsntWhereAWindowCanBeIsLeftOut(string content)
    {
        await WriteAsync(content);

        Assert.Null((await Store().LoadAsync(Cancellation)).Window);
    }

    [Fact]
    public async Task AWindowWithoutMaximizedWasntMaximized()
    {
        await WriteAsync("""{ "window": { "x": 1, "y": 2, "width": 800, "height": 600, "maximized": "yes" } }""");

        Assert.Equal(new WindowPlacement(1, 2, 800, 600, maximized: false), (await Store().LoadAsync(Cancellation)).Window);
    }

    [Fact]
    public async Task ValuesThatArentWhatTheyShouldBeAreLeftOut()
    {
        await WriteAsync("""{ "menuCollapsed": "yes", "screen": 3, "browseSearches": [ "skyui", 2, null, "", "armour" ] }""");

        UiState state = await Store().LoadAsync(Cancellation);

        Assert.Equal(new UiState { BrowseSearches = ["skyui", "armour"] }, state);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task QuickSearchesThatArentTextOrAListAreLeftOut()
    {
        await WriteAsync("""{ "quickSearches": [ "dragon", 2, null, " ", "pawn" ], "browseSearches": "skyui" }""");
        UiState listed = await Store().LoadAsync(Cancellation);
        await WriteAsync("""{ "quickSearches": "dragon" }""");

        Assert.Equal(new UiState { QuickSearches = ["dragon", "pawn"] }, listed);
        Assert.Equal(UiState.None, await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task OneQuickSearchIsKeptAndNoneLeavesTheFile()
    {
        await Store().SaveAsync(new UiState { QuickSearches = ["dragon"] }, Cancellation);

        Assert.Equal(new UiState { QuickSearches = ["dragon"] }, await Store().LoadAsync(Cancellation));

        await Store().SaveAsync(new UiState { Screen = "updates" }, Cancellation);
        Assert.DoesNotContain("quickSearches", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesThatArentAListAreLeftOut()
    {
        await WriteAsync("""{ "browseSearches": "skyui", "menuCollapsed": true }""");

        Assert.Equal(new UiState { MenuCollapsed = true }, await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public void RefusesARelativePath()
    {
        Assert.Throws<ArgumentException>(() => new JsonUiStateStore(Path.Combine("App Data", "ui-state.json"), _logger));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddUiStateFile("ui-state.json"));
    }

    [Fact]
    public async Task IsRegisteredAsTheUiStateStore()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddUiStateFile(FilePath);
        await using ServiceProvider provider = services.BuildServiceProvider();

        IUiStateStore store = provider.GetRequiredService<IUiStateStore>();
        await store.SaveAsync(new UiState { MenuCollapsed = false }, Cancellation);

        Assert.Equal(FilePath, store.Location);
        Assert.Equal(new UiState { MenuCollapsed = false }, await store.LoadAsync(Cancellation));
    }

    private JsonUiStateStore Store() => new(FilePath, _logger);

    private async Task WriteAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
    }
}
