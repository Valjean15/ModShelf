using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library screen over fakes, with a Nexus Mods catalog that names Skyrim Special Edition. Its work starts with
/// <see cref="StartAsync"/> and stops when the harness is disposed.
/// </summary>
internal sealed class LibraryHarness : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public LibraryHarness()
    {
        Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Screen = new LibraryViewModel(
            Library,
            Lookup,
            Rescan,
            LibraryDownloads,
            Links,
            Watchlists,
            Storage,
            Settings,
            Browser,
            Folders,
            Clipboard,
            Dialogs,
            new FakeSourceRegistry(Catalog),
            Pictures.Pictures,
            UiThread,
            Time,
            Logger);
    }

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedRescan Rescan { get; } = new();

    public ScriptedLookup Lookup { get; } = new();

    public ScriptedLibraryDownloads LibraryDownloads { get; } = new();

    public ScriptedLinkDownloads Links { get; } = new();

    public ScriptedWatchlists Watchlists { get; } = new();

    public FakeLibraryStorage Storage { get; } = new();

    public FakeSettingsService Settings { get; } = new();

    public ScriptedBrowser Browser { get; } = new();

    public ScriptedFolders Folders { get; } = new();

    public RecordingClipboard Clipboard { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public ScriptedCatalog Catalog { get; } = new(TestData.Nexus, "Nexus Mods");

    public TestPictures Pictures { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public RecordingLogger<LibraryViewModel> Logger { get; } = new();

    public LibraryViewModel Screen { get; }

    /// <summary>
    /// The screen's work, once started.
    /// </summary>
    public Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Adds <paramref name="mods"/> to the library, starts the screen's work, as the main window does, and waits until
    /// it has listed the library and follows it. From then on, each change reaches the screen before it returns.
    /// </summary>
    public async Task StartAsync(params LibraryMod[] mods)
    {
        Library.Add(mods);

        // Off the test's thread, so the work continues wherever the library publishes, as with no UI in the way.
        Work = Task.Run(() => Screen.StartAsync(_stopping.Token), TestContext.Current.CancellationToken);
        await Library.WaitedOn.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The screen's row for the mod, opening its game when another one, or none, is open, and waiting for its check.
    /// </summary>
    public LibraryModViewModel Row(LibraryMod mod)
    {
        if (Screen.OpenGame?.Game != mod.Mod.Game)
        {
            Open(Game(mod.Mod.Game));
        }

        return Assert.Single(Screen.GameMods, row => row.Mod == mod.Mod);
    }

    /// <summary>
    /// The game among the screen's games.
    /// </summary>
    public LibraryGameViewModel Game(GameRef game) => Assert.Single(Screen.Games, shown => shown.Game == game);

    /// <summary>
    /// The games and their mods as text: "# " and a game's name, then its mods' names as opening it shows them. The
    /// screen then shows what it showed before.
    /// </summary>
    public IReadOnlyList<string> Lines()
    {
        LibraryGameViewModel? open = Screen.OpenGame;
        List<string> lines = [];
        foreach (LibraryGameViewModel game in Screen.Games.ToList())
        {
            lines.Add($"# {game.Name}");
            Open(game);
            lines.AddRange(Screen.GameMods.Select(mod => mod.Name));
        }

        if (open is null)
        {
            Screen.ShowGamesCommand.Execute(null);
        }
        else
        {
            Open(open);
        }

        return lines;
    }

    /// <summary>
    /// Opens the game and waits for its check, whose result the UI thread shows: here, the check's own thread, which
    /// would race with the test's next step.
    /// </summary>
    private void Open(LibraryGameViewModel game)
    {
        game.OpenCommand.Execute(null);
        Assert.True(Screen.Checking.Wait(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// Stops the screen's work, as closing the app does. Once is enough.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
