using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Catalog;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The Browse screen over fakes: Nexus Mods, which searches three games, describes their mods, and keeps tracked mods.
/// Its work starts with <see cref="StartAsync"/> and stops when the harness is disposed.
/// </summary>
internal sealed class BrowseHarness : IDisposable
{
    public static readonly GameInfo Skyrim = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "Skyrim Special Edition");

    public static readonly GameInfo Stardew = new(new GameRef(TestData.Nexus, "stardewvalley"), "Stardew Valley");

    public static readonly GameInfo Fallout = new(new GameRef(TestData.Nexus, "fallout4"), "Fallout 4");

    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public BrowseHarness()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Lookup.Searchable.Add(TestData.Nexus);
        Lookup.Games[TestData.Nexus] = [Stardew, Skyrim, Fallout];
        Watchlists.Tracking.Add(TestData.Nexus);
        Screen = new BrowseViewModel(
            Lookup,
            Library,
            LibraryDownloads,
            Links,
            Watchlists,
            new FakeSourceRegistry(Catalog),
            Browser,
            Dialogs,
            Navigation,
            UiThread,
            Time,
            Logger);
    }

    public ScriptedLookup Lookup { get; } = new();

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedLibraryDownloads LibraryDownloads { get; } = new();

    public ScriptedLinkDownloads Links { get; } = new();

    public ScriptedWatchlists Watchlists { get; } = new();

    public ScriptedCatalog Catalog { get; } = new(TestData.Nexus, "Nexus Mods");

    public ScriptedBrowser Browser { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public NavigationService Navigation { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public RecordingLogger<BrowseViewModel> Logger { get; } = new();

    public BrowseViewModel Screen { get; }

    /// <summary>
    /// The screen's work, once started.
    /// </summary>
    public Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// A mod as its site describes it, with its page on Nexus Mods.
    /// </summary>
    public static ModInfo Info(ModRef mod, string name, bool isAvailable = true)
        => new(
            mod,
            name,
            summary: $"All about {name}.",
            author: "An Author",
            version: "1.2",
            pageUrl: new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}"),
            updatedAt: TestData.Now.AddDays(-3),
            isAvailable: isAvailable,
            downloads: 2134567,
            endorsements: 1);

    /// <summary>
    /// One of a mod's files on its site, uploaded <paramref name="daysAgo"/> days ago, with its page.
    /// </summary>
    public static ModFile File(ModRef mod, string id, ModFileCategory category = ModFileCategory.Main, int daysAgo = 1, bool hasPage = true)
        => new(
            new ModFileRef(mod, id),
            $"File {id}",
            $"file-{id}.7z",
            $"1.{id}",
            category,
            2048,
            TestData.Now.AddDays(-daysAgo),
            [],
            hasPage ? new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}?tab=files&file_id={id}") : null);

    public static ModPage Page(ModInfo mod, params ModFile[] files)
        => new(mod.Mod.Game == Skyrim.Game ? Skyrim : Stardew, mod, files, [new ModChangelog("1.2", ["Fixed the menus.", "Faster."])], null);

    /// <summary>
    /// Mods of the game, named "Mod 1", "Mod 2" and so on, that its searches find.
    /// </summary>
    public List<ModInfo> Searchable(GameInfo game, int count)
    {
        List<ModInfo> mods = [.. Enumerable.Range(1, count).Select(id => Info(new ModRef(game.Game, $"{id}"), $"Mod {id}"))];
        Lookup.Mods[game.Game] = mods;
        return mods;
    }

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
    /// Shows the screen, as the sidebar does, and waits for the games and the search they start.
    /// </summary>
    public async Task ShowAsync()
    {
        Navigation.NavigateTo(Presentation.Services.Screen.Browse);
        await Screen.GamesWork;
        await Screen.Listing;
    }

    /// <summary>
    /// Opens the mod's page from a link to it, as pasting the link does, and returns it.
    /// </summary>
    public async Task<ModPageViewModel> OpenAsync(ModPage page)
    {
        Uri link = page.Mod.PageUrl!;
        Lookup.LinkedPages[link] = page;
        Screen.SearchText = link.AbsoluteUri;
        await Screen.SearchCommand.ExecuteAsync(null);
        return Screen.Mod ?? throw new InvalidOperationException($"The mod didn't open: {Screen.Problem}");
    }

    /// <summary>
    /// The results' names, in order.
    /// </summary>
    public IReadOnlyList<string> Names() => [.. Screen.Results.Select(row => row.Name)];

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
