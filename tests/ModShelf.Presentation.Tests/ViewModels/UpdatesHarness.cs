using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The Updates screen over fakes, with a Nexus Mods catalog for changelogs and a site without one. Its work starts with
/// <see cref="StartAsync"/> and stops when the harness is disposed.
/// </summary>
internal sealed class UpdatesHarness : IDisposable
{
    public static readonly SourceId Plain = new("plain");

    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public UpdatesHarness()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        FakeSourceRegistry sources = new(Catalog, new FakeSource(Plain, "Plain Site"));
        Screen = new UpdatesViewModel(
            Library,
            Checker,
            Downloads,
            sources,
            AppLookup.Over(sources),
            Dialogs,
            UiThread,
            Time,
            Logger);
    }

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedUpdateChecker Checker { get; } = new();

    public ScriptedLibraryDownloads Downloads { get; } = new();

    public ScriptedCatalog Catalog { get; } = new(TestData.Nexus, "Nexus Mods");

    public ScriptedDialogs Dialogs { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public RecordingLogger<UpdatesViewModel> Logger { get; } = new();

    public UpdatesViewModel Screen { get; }

    /// <summary>
    /// The screen's work, once started.
    /// </summary>
    public Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// A watched mod with one file, 1, checked when told, and updates of it: files whose IDs are given, uploaded a day
    /// apart, the last given the newest. Each update replaces file 1, and has its version as its ID.
    /// </summary>
    public static LibraryMod ModWithUpdates(ModRef mod, string name, DateTimeOffset? checkedAt, params string[] updates)
    {
        LibraryFile file = TestData.LibraryFile(mod, "1", $"{name}-1.7z");
        return LibraryMod.Restore(
            mod,
            name,
            null,
            null,
            isWatched: true,
            TestData.Now.AddDays(-30),
            checkedAt,
            [file],
            [.. updates.Select((id, index) => Update(mod, id, TestData.Now.AddDays(index - updates.Length)))]);
    }

    public static LibraryUpdate Update(ModRef mod, string id, DateTimeOffset uploadedAt, Uri? page = null)
        => new(
            new ModFileRef(mod, id),
            $"Update {id}",
            $"update-{id}.7z",
            id,
            ModFileCategory.Main,
            uploadedAt,
            new ModFileRef(mod, "1"),
            TestData.Now,
            page ?? new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}?tab=files&file_id={id}"));

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
    /// The screen's row for the update.
    /// </summary>
    public UpdateViewModel Row(string fileId) => Assert.Single(Screen.Updates, row => row.File.Id == fileId);

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
