using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Library;

/// <summary>
/// What the user keeps of a library mod: a pin at the version it has, updates skipped until a newer one comes, and a
/// note; and the updates offered, which leave out a pinned mod's and the skipped ones. SkyUI has 5.2 (file 1), with
/// updates 2, which replaces it, and 3, a new optional file.
/// </summary>
public sealed class LibraryModPinsTests
{
    private static readonly DateTimeOffset Later = TestData.CreatedAt.AddDays(3);

    [Fact]
    public void AModStartsUnpinnedWithoutANoteOrUpdatesSkipped()
    {
        LibraryMod mod = LibraryMod.Watch(TestData.SkyUi, "SkyUI", null, null, TestData.CreatedAt);

        Assert.Equal((false, (string?)null), (mod.IsPinned, mod.Note));
        Assert.Empty(mod.SkippedUpdates);
        Assert.Empty(mod.OfferedUpdates);
    }

    [Fact]
    public void APinnedModOffersNoUpdateButKeepsThem()
    {
        LibraryMod mod = WithUpdates();

        LibraryMod pinned = mod.WithPinned(true);

        Assert.True(pinned.IsPinned);
        Assert.Empty(pinned.OfferedUpdates);
        Assert.Equal(["2", "3"], pinned.Updates.Select(update => update.File.Id));
        Assert.Equal(["2", "3"], pinned.WithPinned(false).OfferedUpdates.Select(update => update.File.Id));
        Assert.False(mod.IsPinned);
    }

    [Fact]
    public void PinningAsItIsChangesNothing()
    {
        LibraryMod mod = WithUpdates();

        Assert.Same(mod, mod.WithPinned(false));
        LibraryMod pinned = mod.WithPinned(true);
        Assert.Same(pinned, pinned.WithPinned(true));
    }

    [Fact]
    public void ASkippedUpdateIsntOffered()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true);

        Assert.Equal([Ref("2")], mod.SkippedUpdates);
        Assert.Equal(["3"], mod.OfferedUpdates.Select(update => update.File.Id));
        Assert.Equal(2, mod.Updates.Count);

        LibraryMod offered = mod.WithUpdateSkipped(Ref("2"), false);

        Assert.Empty(offered.SkippedUpdates);
        Assert.Equal(["2", "3"], offered.OfferedUpdates.Select(update => update.File.Id));
    }

    [Fact]
    public void SkippingAsItIsChangesNothing()
    {
        LibraryMod mod = WithUpdates();
        LibraryMod skipped = mod.WithUpdateSkipped(Ref("2"), true);

        Assert.Same(mod, mod.WithUpdateSkipped(Ref("2"), false));
        Assert.Same(skipped, skipped.WithUpdateSkipped(Ref("2"), true));
        Assert.Same(skipped, skipped.WithUpdateSkipped(Ref("9"), false));
    }

    [Fact]
    public void SkipsOnlyItsOwnUpdates()
    {
        LibraryMod mod = WithUpdates();

        Assert.Throws<ArgumentException>(() => mod.WithUpdateSkipped(Ref("1"), true));
        Assert.Throws<ArgumentException>(() => mod.WithUpdateSkipped(Ref("9"), true));
        Assert.Throws<ArgumentException>(() => mod.WithUpdateSkipped(new ModFileRef(new ModRef(TestData.SkyUi.Game, "3863"), "2"), true));
    }

    [Fact]
    public void ASkippedUpdateStaysSkippedWhileItsFound()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true);

        LibraryMod checkedAgain = mod.WithUpdates([Update("2", "1"), Update("3")]);

        Assert.Equal([Ref("2")], checkedAgain.SkippedUpdates);
    }

    [Fact]
    public void ANewerUpdateEndsTheSkip()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true);

        LibraryMod newer = mod.WithUpdates([Update("4", "1"), Update("3")]);

        Assert.Empty(newer.SkippedUpdates);
        Assert.Equal(["4", "3"], newer.OfferedUpdates.Select(update => update.File.Id));
    }

    [Fact]
    public void ASkippedUpdateDownloadedAfterAllIsntSkippedAnyMore()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true).WithUpdateSkipped(Ref("3"), true);

        LibraryMod downloaded = mod.WithFile(File("2", "SkyUI_5_3_SE.7z"));

        Assert.Equal([Ref("3")], downloaded.SkippedUpdates);
    }

    [Fact]
    public void AnUpdateOfAFileDeletedLeavesWithItsSkip()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true).WithUpdateSkipped(Ref("3"), true);

        LibraryMod deleted = mod.WithoutFile(Ref("1"))!;

        Assert.Equal([Ref("3")], deleted.SkippedUpdates);
    }

    [Fact]
    public void UnwatchingEndsTheSkipsButKeepsThePinAndTheNote()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("2"), true).WithPinned(true).WithNote("Stay on 5.2.");

        LibraryMod unwatched = mod.Unwatched()!;

        Assert.Empty(unwatched.SkippedUpdates);
        Assert.Equal((true, "Stay on 5.2."), (unwatched.IsPinned, unwatched.Note));
        Assert.Equal((true, "Stay on 5.2."), (unwatched.Watched().IsPinned, unwatched.Watched().Note));
    }

    [Fact]
    public void ThePinTheNoteAndTheSkipsLastThroughOtherChanges()
    {
        LibraryMod mod = WithUpdates().WithUpdateSkipped(Ref("3"), true).WithPinned(true).WithNote("Stay on 5.2.");

        LibraryMod changed = mod
            .WithDetails("SkyUI SE", "schlangster", null)
            .Checked(Later)
            .WithFile(File("1", "moved/SkyUI_5_2_SE.7z"))
            .WithFile(File("5", "SkyUI_extra.7z"));

        Assert.Equal((true, "Stay on 5.2."), (changed.IsPinned, changed.Note));
        Assert.Equal([Ref("3")], changed.SkippedUpdates);
    }

    [Theory]
    [InlineData("  Stay on 5.2.  ", "Stay on 5.2.")]
    [InlineData("Line one\nLine two", "Line one\nLine two")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void KeepsANoteTrimmedAndABlankOneIsNone(string? note, string? kept)
    {
        Assert.Equal(kept, WithUpdates().WithNote("Old.").WithNote(note).Note);
    }

    [Fact]
    public void ANoteHoldsAtMostTwoThousandCharacters()
    {
        LibraryMod mod = WithUpdates();

        Assert.Equal(LibraryMod.MaxNoteLength, mod.WithNote(new string('a', 2000)).Note!.Length);
        Assert.Equal(2000, mod.WithNote($"  {new string('a', 2000)}  ").Note!.Length);
        ArgumentException refused = Assert.Throws<ArgumentException>(() => mod.WithNote(new string('a', 2001)));
        Assert.StartsWith("A note holds at most 2,000 characters; this one has 2,001.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoresThePinTheNoteAndTheSkips()
    {
        LibraryMod mod = Restore(isPinned: true, note: "Stay on 5.2.", skipped: [Ref("3")]);

        Assert.Equal((true, "Stay on 5.2."), (mod.IsPinned, mod.Note));
        Assert.Equal([Ref("3")], mod.SkippedUpdates);
    }

    [Fact]
    public void RefusesToRestoreWhatNoModHas()
    {
        Assert.Throws<ArgumentException>(() => Restore(skipped: [Ref("1")]));
        Assert.Throws<ArgumentException>(() => Restore(skipped: [Ref("3"), Ref("3")]));
        Assert.Throws<ArgumentException>(() => Restore(skipped: [null!]));
        Assert.Throws<ArgumentException>(() => Restore(note: new string('a', 2001)));
    }

    private static ModFileRef Ref(string id) => new(TestData.SkyUi, id);

    private static LibraryFile File(string id, string path = "SkyUI_5_2_SE.7z")
        => new(Ref(id), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.CreatedAt, path, 2_500_000, Later);

    private static LibraryUpdate Update(string id, string? replaces = null)
        => new(Ref(id), $"SkyUI {id}", $"SkyUI_{id}.7z", null, ModFileCategory.Main, TestData.CreatedAt, replaces is null ? null : Ref(replaces), Later);

    private static LibraryMod WithUpdates()
        => LibraryMod.Watch(TestData.SkyUi, "SkyUI", null, null, TestData.CreatedAt).WithFile(File("1")).WithUpdates([Update("2", "1"), Update("3")]);

    private static LibraryMod Restore(bool isPinned = false, string? note = null, ModFileRef[]? skipped = null)
        => LibraryMod.Restore(
            TestData.SkyUi, "SkyUI", null, null, true, TestData.CreatedAt, null, [File("1")], [Update("2", "1"), Update("3")], false, isPinned, note, skipped);
}
