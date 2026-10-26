using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Sources.Abstractions.Tests.Updates;

public sealed class ModUpdateTests
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    [Fact]
    public void KeepsWhatTheSourceSays()
    {
        DateTimeOffset activity = new(2026, 10, 23, 16, 28, 23, TimeSpan.Zero);
        DateTimeOffset files = new(2025, 4, 16, 2, 45, 43, TimeSpan.Zero);
        Uri page = new("https://www.nexusmods.com/stardewvalley/mods/2400");

        ModUpdate update = new(Smapi, activity, files, page);

        Assert.Equal((Smapi, activity, (DateTimeOffset?)files, page), (update.Mod, update.LastActivityAt, update.FilesUpdatedAt, update.PageUrl));
    }

    [Fact]
    public void NeedsNeitherTheFilesTimeNorAPage()
    {
        ModUpdate update = new(Smapi, DateTimeOffset.UnixEpoch);

        Assert.Null(update.FilesUpdatedAt);
        Assert.Null(update.PageUrl);
    }

    [Fact]
    public void NeedsAMod()
    {
        Assert.Throws<ArgumentNullException>(() => new ModUpdate(null!, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("nxm://stardewvalley/mods/2400/files/160380")]
    public void RefusesAPageThatIsntAWebLink(string page)
    {
        Assert.Throws<ArgumentException>(() => new ModUpdate(Smapi, DateTimeOffset.UnixEpoch, pageUrl: new Uri(page)));
    }

    [Fact]
    public void RefusesARelativePage()
    {
        Assert.Throws<ArgumentException>(() => new ModUpdate(Smapi, DateTimeOffset.UnixEpoch, pageUrl: new Uri("/stardewvalley/mods/2400", UriKind.Relative)));
    }
}
