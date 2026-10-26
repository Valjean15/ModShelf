using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Tests.Catalog;

public sealed class ModChangelogTests
{
    [Fact]
    public void KeepsTheChangesInOrderAndLeavesOutBlankOnes()
    {
        ModChangelog changelog = new(" 4.5.2 ", ["Fixed the crash on start. ", "", "  ", "Added a setting."]);

        Assert.Equal("4.5.2", changelog.Version);
        Assert.Equal(["Fixed the crash on start.", "Added a setting."], changelog.Changes);
    }

    [Fact]
    public void KeepsItsOwnCopyOfTheChanges()
    {
        List<string> changes = ["Fixed the crash on start."];

        ModChangelog changelog = new("4.5.2", changes);
        changes.Add("Added later.");

        Assert.Single(changelog.Changes);
    }
}
