using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// What the user keeps of a library mod, as the Library shows and changes it: a pin, which offers none of its updates,
/// updates skipped, and a note. SkyUI has file 1, at 5.1, and updates 2 and 3, which replace it.
/// </summary>
public sealed class LibraryPinsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public LibraryPinsTests()
    {
        // On disk, so no file is missing.
        _harness.Storage.Files.Add(LibraryPaths.Resolve(TestData.LibraryRoot, WithUpdates().Files[0].RelativePath));
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task APinnedModSaysItsVersionAndOffersNoUpdate()
    {
        await _harness.StartAsync(WithUpdates().WithPinned(true));

        LibraryModViewModel row = _harness.Row(WithUpdates());

        Assert.Equal(["Pinned at 5.1 (Note)"], row.Statuses.Select(status => $"{status.Text} ({status.Kind})"));
        Assert.Equal((0, false, true), (row.UpdateCount, row.HasUpdates, row.IsPinned));
        Assert.Empty(row.DownloadableUpdates);
        Assert.Equal((0, "Up to date"), (_harness.Game(SkyUi.Game).UpdateCount, _harness.Game(SkyUi.Game).UpdatesText));
    }

    [Fact]
    public async Task APinnedModWithoutAVersionSaysPinned()
    {
        LibraryMod mod = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now).WithPinned(true);
        await _harness.StartAsync(mod);

        Assert.Equal(["Pinned (Note)", "Watched only (Note)"], _harness.Row(mod).Statuses.Select(status => $"{status.Text} ({status.Kind})"));
    }

    [Fact]
    public async Task ASkippedUpdateIsntCountedOrDownloaded()
    {
        await _harness.StartAsync(WithUpdates().WithUpdateSkipped(new ModFileRef(SkyUi, "3"), true));

        LibraryModViewModel row = _harness.Row(WithUpdates());

        Assert.Equal(["Update to 5.2 (Update)", "1 update skipped (Note)"], row.Statuses.Select(status => $"{status.Text} ({status.Kind})"));
        Assert.Equal(1, row.UpdateCount);
        Assert.Equal(["2"], row.DownloadableUpdates.Select(update => update.File.Id));
        Assert.True(row.HasSkipped);
    }

    [Fact]
    public async Task APinnedModDoesntSayItsSkippedUpdatesAndTwoSaySo()
    {
        LibraryMod skipped = WithUpdates().WithUpdateSkipped(new ModFileRef(SkyUi, "2"), true).WithUpdateSkipped(new ModFileRef(SkyUi, "3"), true);
        await _harness.StartAsync(skipped);
        LibraryModViewModel row = _harness.Row(skipped);

        Assert.Equal(["2 updates skipped (Note)"], row.Statuses.Select(status => $"{status.Text} ({status.Kind})"));

        await row.PinCommand.ExecuteAsync(null);

        Assert.Equal(["Pinned at 5.1 (Note)"], row.Statuses.Select(status => $"{status.Text} ({status.Kind})"));
    }

    [Fact]
    public async Task OffersTheSkippedUpdatesAgain()
    {
        LibraryMod skipped = WithUpdates().WithUpdateSkipped(new ModFileRef(SkyUi, "2"), true).WithUpdateSkipped(new ModFileRef(SkyUi, "3"), true);
        await _harness.StartAsync(skipped);
        LibraryModViewModel row = _harness.Row(skipped);

        await row.OfferSkippedCommand.ExecuteAsync(null);

        Assert.Equal(["Offer 2", "Offer 3"], _harness.Library.Asked);
        Assert.Equal((2, false), (row.UpdateCount, row.HasSkipped));
        Assert.False(row.OfferSkippedCommand.CanExecute(null));
    }

    [Fact]
    public async Task PinsAndUnpinsFromItsMenu()
    {
        await _harness.StartAsync(WithUpdates());
        LibraryModViewModel row = _harness.Row(WithUpdates());
        Assert.False(row.UnpinCommand.CanExecute(null));

        await row.PinCommand.ExecuteAsync(null);

        Assert.True(row.IsPinned);
        Assert.Equal((false, true), (row.PinCommand.CanExecute(null), row.UnpinCommand.CanExecute(null)));
        Assert.Equal(0, row.UpdateCount);

        await row.UnpinCommand.ExecuteAsync(null);

        Assert.Equal(["Pin 12604", "Unpin 12604"], _harness.Library.Asked);
        Assert.Equal((false, 2), (row.IsPinned, row.UpdateCount));
    }

    [Fact]
    public async Task AModWithUpdatesPinnedLeavesTheFilterOfModsWithUpdates()
    {
        await _harness.StartAsync(WithUpdates().WithPinned(true));
        _harness.Row(WithUpdates());

        Screen.ModFilters[0].IsOn = true;

        Assert.Empty(Screen.GameMods);
    }

    [Fact]
    public async Task EditsTheNoteStartingWithTheOneItHas()
    {
        await _harness.StartAsync(WithUpdates().WithNote("Stay on 5.1."));
        LibraryModViewModel row = _harness.Row(WithUpdates());
        Assert.Equal((true, "Edit the note…"), (row.HasNote, row.EditNoteText));
        _harness.Dialogs.Answers.Enqueue("Stay on 5.1: 5.2 breaks the map.\nCheck again in May.");

        await row.EditNoteCommand.ExecuteAsync(null);

        TextQuestion asked = Assert.Single(_harness.Dialogs.Questions);
        Assert.Equal(
            ("Note", "A note of SkyUI, such as why it's pinned. Leave it empty to remove it.", "Save", "Stay on 5.1.", true, 2000),
            (asked.Title, asked.Message, asked.ConfirmLabel, asked.Text, asked.IsMultiline, asked.MaxLength));
        Assert.Equal("Stay on 5.1: 5.2 breaks the map.\nCheck again in May.", row.Note);
    }

    [Fact]
    public async Task AnEmptyNoteRemovesItAndCancellingKeepsIt()
    {
        await _harness.StartAsync(WithUpdates().WithNote("Stay on 5.1."));
        LibraryModViewModel row = _harness.Row(WithUpdates());
        _harness.Dialogs.Answers.Enqueue(null);
        _harness.Dialogs.Answers.Enqueue(string.Empty);

        await row.EditNoteCommand.ExecuteAsync(null);

        Assert.Equal("Stay on 5.1.", row.Note);
        Assert.Empty(_harness.Library.Asked);

        await row.EditNoteCommand.ExecuteAsync(null);

        Assert.Equal(((string?)null, false, "Add a note…"), (row.Note, row.HasNote, row.EditNoteText));
        Assert.Equal("Stay on 5.1.", _harness.Dialogs.Questions[1].Text);
    }

    [Fact]
    public async Task AModThatLeftMeanwhileIsntPinnedOrNotedAndNothingIsSaid()
    {
        await _harness.StartAsync(WithUpdates());
        LibraryModViewModel row = _harness.Row(WithUpdates());
        _harness.Library.Refusal = new KeyNotFoundException("No mod 12604.");
        _harness.Dialogs.Answers.Enqueue("A note.");

        await row.PinCommand.ExecuteAsync(null);
        await row.EditNoteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task OfferingSkippedUpdatesOfAModThatLeftSaysNothing()
    {
        LibraryMod skipped = WithUpdates().WithUpdateSkipped(new ModFileRef(SkyUi, "2"), true);
        await _harness.StartAsync(skipped);
        LibraryModViewModel row = _harness.Row(skipped);
        _harness.Library.Refusal = new KeyNotFoundException("No mod 12604.");

        await row.OfferSkippedCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task APinThatFailsUnexpectedlyIsLoggedAndSaid()
    {
        await _harness.StartAsync(WithUpdates());
        LibraryModViewModel row = _harness.Row(WithUpdates());
        _harness.Library.Refusal = new InvalidOperationException("Something broke.");

        await row.PinCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Messages);
        Assert.StartsWith("Error: Pin of mod 12604", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    private static LibraryMod WithUpdates()
        => TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "SkyUI-5.1.7z", "5.1"))
            .WithUpdates([Update("2", "5.2", 1), Update("3", "5.3", 0)]);

    private static LibraryUpdate Update(string id, string version, int daysAgo)
        => new(new ModFileRef(SkyUi, id), $"SkyUI {version}", $"SkyUI-{version}.7z", version, ModFileCategory.Main, TestData.Now.AddDays(-daysAgo), new ModFileRef(SkyUi, "1"), TestData.Now, new Uri($"https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id={id}"));
}
