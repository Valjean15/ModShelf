using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Catalog;

public sealed class ModerationTests
{
    private static readonly TestAuthor Admin = new(9);
    private static readonly TestAuthor Author = new(5);

    [Fact]
    public void ReadsTheAdminsRecordedLists()
    {
        Moderation moderation = Moderation.Read(RecordedDegMods.RecordedEvents("relay.degmods.com"), DegModsOptions.DefaultAdmin);

        Assert.Equal(["gore", "loli", "politics", "religion", "shota"], moderation.HiddenTags.Order(StringComparer.Ordinal));
        Assert.Equal([$"{RecordedDegMods.TestPostAuthor}:{RecordedDegMods.BlockedPost}"], moderation.BlockedMods);
        Assert.Equal(2, moderation.BlockedAuthors.Count);

        // Its one moderation tag adds no warning.
        Assert.Empty(moderation.MarkedAdult);
    }

    [Fact]
    public void HidesAModWithAHiddenTagABlockedOneAndABlockedAuthors()
    {
        Moderation moderation = Moderation.Read(
            [
                Admin.Sign(Moderation.ListKind, 100, [["d", Moderation.HiddenTagsList], ["t", "Gore"]]),
                Admin.Sign(Moderation.ListKind, 100, [["d", Moderation.BlockedModsList], ["a", $"31142:{Author.PublicKey}:blocked", "block"], ["a", $"30402:{Author.PublicKey}:old"]]),
                Admin.Sign(Moderation.BlockedAuthorsKind, 100, [["p", new TestAuthor(6).PublicKey]]),
            ],
            Admin.PublicKey);

        Assert.False(moderation.Hides(Mod(Author, "fine")));
        Assert.True(moderation.Hides(Mod(Author, "tagged", "gore")));
        Assert.True(moderation.Hides(Mod(Author, "blocked")));
        Assert.True(moderation.Hides(Mod(Author, "old")));
        Assert.True(moderation.Hides(Mod(new TestAuthor(6), "anything")));
    }

    [Fact]
    public void AlwaysHidesModsSexualizingMinorsWhateverTheAdminsListSays()
    {
        Moderation moderation = Moderation.Read([Admin.Sign(Moderation.ListKind, 100, [["d", Moderation.HiddenTagsList], ["t", "politics"]])], Admin.PublicKey);

        Assert.All(Moderation.AlwaysHiddenTags, tag => Assert.True(moderation.Hides(Mod(Author, "x", tag))));
        Assert.True(new Moderation(new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()).Hides(Mod(Author, "x", Moderation.AlwaysHiddenTags[0])));
    }

    [Fact]
    public void WithoutTheAdminsListsHidesWhatDegModsSiteDoesWithoutThem()
    {
        Moderation moderation = Moderation.Read([], Admin.PublicKey);

        Assert.Equal(Moderation.DefaultHiddenTags.Order(StringComparer.Ordinal), moderation.HiddenTags.Order(StringComparer.Ordinal));
        Assert.Empty(moderation.BlockedMods);
        Assert.Empty(moderation.BlockedAuthors);
        Assert.Equal(Moderation.Default.HiddenTags.Order(StringComparer.Ordinal), moderation.HiddenTags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TakesTheAdminsNewestListOnly()
    {
        Moderation moderation = Moderation.Read(
            [
                Admin.Sign(Moderation.ListKind, 200, [["d", Moderation.HiddenTagsList], ["t", "new"]]),
                Admin.Sign(Moderation.ListKind, 100, [["d", Moderation.HiddenTagsList], ["t", "old"]]),
                Admin.Sign(Moderation.BlockedAuthorsKind, 100, [["p", new TestAuthor(6).PublicKey]]),
                Admin.Sign(Moderation.BlockedAuthorsKind, 200, [["p", new TestAuthor(7).PublicKey]]),
            ],
            Admin.PublicKey);

        Assert.Contains("new", moderation.HiddenTags);
        Assert.DoesNotContain("old", moderation.HiddenTags);
        Assert.Equal([new TestAuthor(7).PublicKey], moderation.BlockedAuthors);
    }

    [Fact]
    public void IgnoresListsAnyoneButTheAdminPublished()
    {
        Moderation moderation = Moderation.Read(
            [Author.Sign(Moderation.ListKind, 100, [["d", Moderation.BlockedModsList], ["a", $"31142:{Author.PublicKey}:x"]])],
            Admin.PublicKey);

        Assert.Empty(moderation.BlockedMods);
    }

    [Fact]
    public void MarksForAdultsAModTheAdminGaveAWarning()
    {
        Moderation moderation = Moderation.Read(
            [
                Admin.Sign(Moderation.MarkKind, 100, [["d", $"31142:{Author.PublicKey}:warned"], ["L", Moderation.Label], ["content-warning", "nsfw"]]),
                Admin.Sign(Moderation.MarkKind, 100, [["d", $"31142:{Author.PublicKey}:cleared"], ["L", Moderation.Label]]),
                Admin.Sign(Moderation.MarkKind, 100, [["d", $"31143:{Author.PublicKey}:jam"], ["content-warning", "nsfw"]]),
            ],
            Admin.PublicKey);

        Assert.True(moderation.Apply(Mod(Author, "warned")).IsAdult);
        Assert.False(moderation.Apply(Mod(Author, "cleared")).IsAdult);
        Assert.Single(moderation.MarkedAdult);
    }

    [Fact]
    public void LeavesOutWhatItHidesAndMarksTheRest()
    {
        Moderation moderation = Moderation.Read(
            [
                Admin.Sign(Moderation.ListKind, 100, [["d", Moderation.HiddenTagsList], ["t", "gore"]]),
                Admin.Sign(Moderation.MarkKind, 100, [["d", $"31142:{Author.PublicKey}:warned"], ["content-warning", "nsfw"]]),
            ],
            Admin.PublicKey);

        IReadOnlyList<DegMod> shown = moderation.Apply([Mod(Author, "fine"), Mod(Author, "warned"), Mod(Author, "hidden", "gore")]);

        Assert.Equal(["fine", "warned"], shown.Select(mod => mod.D));
        Assert.Equal([false, true], shown.Select(mod => mod.IsAdult));
    }

    private static DegMod Mod(TestAuthor author, string d, params string[] tags)
    {
        NostrEvent nostrEvent = author.Mod(d, $"Mod {d}", "Game", 100, [], [.. tags.Select(tag => new[] { "t", tag })]);
        return ModEvents.Read(nostrEvent)!;
    }
}
