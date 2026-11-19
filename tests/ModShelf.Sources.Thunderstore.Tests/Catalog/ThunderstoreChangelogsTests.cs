using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Thunderstore.Catalog;

namespace ModShelf.Sources.Thunderstore.Tests.Catalog;

/// <summary>
/// A package's <c>CHANGELOG.md</c> read as each version's changes, by the headings that name a version.
/// </summary>
public sealed class ThunderstoreChangelogsTests
{
    [Fact]
    public void ReadsEachVersionsChangesUnderItsHeading()
    {
        const string Markdown = """
            # Changelog

            Some words before any version.

            ## 1.4.66

            - Shared one adapter.
            - Fixed the battery display.

            ### Fixed
            * The broken trap.

            ## v1.4.65 (2026-11-17)
            1. Faster.
            2) Smaller.

            # 1.4.64 #
            Plain line.
            """;

        IReadOnlyList<ModChangelog> changelogs = ThunderstoreChangelogs.Parse(Markdown);

        Assert.Equal(["1.4.66", "1.4.65", "1.4.64"], changelogs.Select(changelog => changelog.Version));
        Assert.Equal(["Shared one adapter.", "Fixed the battery display.", "The broken trap."], changelogs[0].Changes);
        Assert.Equal(["Faster.", "Smaller."], changelogs[1].Changes);
        Assert.Equal(["Plain line."], changelogs[2].Changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Just some notes, with no version headings.\n- A change.")]
    [InlineData("## Unreleased\n- Something.")]
    public void ReadsNothingWithoutAHeadingThatNamesAVersion(string? markdown)
    {
        Assert.Empty(ThunderstoreChangelogs.Parse(markdown));
    }

    [Fact]
    public void KeepsTheFirstSectionOfAVersionNamedTwice()
    {
        IReadOnlyList<ModChangelog> changelogs = ThunderstoreChangelogs.Parse("## 1.0.1\n- New.\n## 1.0.1\n- Repeated.\n## 1.0.0\n- First.");

        Assert.Equal([("1.0.1", "New."), ("1.0.0", "First.")], changelogs.Select(changelog => (changelog.Version, Assert.Single(changelog.Changes))));
    }
}
