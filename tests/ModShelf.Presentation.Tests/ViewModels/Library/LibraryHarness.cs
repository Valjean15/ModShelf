using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Library;
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
            Settings,
            Browser,
            Folders,
            Dialogs,
            new FakeSourceRegistry(Catalog),
            UiThread,
            Time,
            Logger);
    }

    public InMemoryModLibrary Library { get; } = new();

    public FakeSettingsService Settings { get; } = new();

    public ScriptedBrowser Browser { get; } = new();

    public ScriptedFolders Folders { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public ScriptedCatalog Catalog { get; } = new(TestData.Nexus, "Nexus Mods");

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
    /// The screen's row for the mod.
    /// </summary>
    public LibraryModViewModel Row(LibraryMod mod) => Assert.Single(Screen.Rows.OfType<LibraryModViewModel>(), row => row.Mod == mod.Mod);

    /// <summary>
    /// The rows as text: "# " and a game's name for its heading, then its mods' names.
    /// </summary>
    public IReadOnlyList<string> Lines()
        => [.. Screen.Rows.Select(row => row switch
        {
            LibraryGameViewModel game => $"# {game.Name}",
            LibraryModViewModel mod => mod.Name,
            _ => throw new InvalidOperationException("Unknown row."),
        })];

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
