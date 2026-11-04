using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Settings;

/// <summary>
/// A game's own settings, Browse's defaults, and what every game's settings give a game.
/// </summary>
public sealed class GameSettingsTests
{
    private static readonly FolderPattern Flat = FolderPattern.Parse("{mod}/{file}");
    private static readonly GameRef Stardew = new(new SourceId("nexus"), "stardewvalley");
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");
    private static readonly GameRef X = new(new SourceId("nexus"), "x");

    [Fact]
    public void AGameWithoutSettingsOfItsOwnUsesEveryGames()
    {
        AppSettings settings = new()
        {
            LibraryFolders = Flat,
            OldVersions = OldVersionRule.Archive,
            UpdateInterval = TimeSpan.FromHours(1),
            NotifyOfUpdates = false,
        };

        Assert.Same(GameSettings.None, settings.GameFor(Stardew));
        Assert.Equal(
            (Flat, OldVersionRule.Archive, TimeSpan.FromHours(1), false, true, false),
            (settings.FoldersFor(Stardew), settings.OldVersionsFor(Stardew), settings.IntervalFor(Stardew),
                settings.NotifiesFor(Stardew), settings.ChecksUpdatesOf(Stardew), settings.IsHidden(Stardew)));
    }

    [Fact]
    public void EveryGamesSettingsLeftOutAreTheirDefaults()
    {
        AppSettings settings = new();

        Assert.Equal(
            (FolderPattern.Default, OldVersionRule.Default, UpdateInterval.Default, true, true, false),
            (settings.FoldersFor(X), settings.OldVersionsFor(X), settings.IntervalFor(X), settings.NotifiesFor(X), settings.ChecksUpdatesOf(X),
                settings.IsHidden(X)));
    }

    [Fact]
    public void AGamesOwnSettingsComeFirstInAnyCase()
    {
        GameSettings own = new()
        {
            Folders = Flat,
            OldVersions = OldVersionRule.KeepAll,
            CheckUpdates = false,
            UpdateInterval = TimeSpan.FromDays(1),
            Notify = true,
            Hidden = true,
        };
        AppSettings settings = new()
        {
            OldVersions = OldVersionRule.Archive,
            UpdateInterval = TimeSpan.FromHours(1),
            NotifyOfUpdates = false,
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [new GameRef(new SourceId("nexus"), "StardewValley")] = own },
        };

        Assert.Same(own, settings.GameFor(Stardew));
        Assert.Equal(
            (Flat, OldVersionRule.KeepAll, TimeSpan.FromDays(1), true, false, true),
            (settings.FoldersFor(new GameRef(new SourceId("nexus"), "STARDEWVALLEY")), settings.OldVersionsFor(Stardew), settings.IntervalFor(Stardew),
                settings.NotifiesFor(Stardew), settings.ChecksUpdatesOf(Stardew), settings.IsHidden(Stardew)));
        Assert.Equal((OldVersionRule.Archive, true), (settings.OldVersionsFor(Skyrim), settings.ChecksUpdatesOf(Skyrim)));
    }

    [Fact]
    public void AGamesOwnSettingsAreFoundWhateverCaseTheGamesWereKeptIn()
    {
        GameSettings own = new() { Notify = false };

        // Kept without the settings' comparer, as a test or a caller might build them.
        AppSettings settings = new() { Games = new Dictionary<GameRef, GameSettings> { [new GameRef(new SourceId("nexus"), "StardewValley")] = own } };

        Assert.Same(own, settings.GameFor(Stardew));
    }

    [Fact]
    public void TheSameKeyOnAnotherSiteIsAnotherGame()
    {
        GameSettings own = new() { Hidden = true };
        AppSettings settings = new()
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [Stardew] = own },
        };

        Assert.True(settings.IsHidden(Stardew));
        Assert.False(settings.IsHidden(new GameRef(new SourceId("example"), "stardewvalley")));
        Assert.Same(GameSettings.None, settings.GameFor(new GameRef(new SourceId("example"), "stardewvalley")));
    }

    [Fact]
    public void GamesAreTheSameByTheirSiteAndTheirKeyInAnyCase()
    {
        IEqualityComparer<GameRef> same = AppSettings.GameComparer;

        Assert.True(same.Equals(Stardew, new GameRef(new SourceId("nexus"), "StardewValley")));
        Assert.Equal(same.GetHashCode(Stardew), same.GetHashCode(new GameRef(new SourceId("nexus"), "StardewValley")));
        Assert.False(same.Equals(Stardew, new GameRef(new SourceId("example"), "stardewvalley")));
        Assert.False(same.Equals(Stardew, Skyrim));
        Assert.False(same.Equals(Stardew, null));
        Assert.True(same.Equals(null, null));
    }

    [Fact]
    public void AGameHasNoSettingsOfItsOwnUntilOneIsSet()
    {
        Assert.True(GameSettings.None.IsNone);
        Assert.True(new GameSettings { Browse = BrowseDefaults.None }.IsNone);
        Assert.False(new GameSettings { Hidden = false }.IsNone);
        Assert.False(new GameSettings { Browse = new BrowseDefaults { PageSize = 50 } }.IsNone);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(7 * 24 * 60 + 1)]
    public void AGamesIntervalIsWithinEveryGamesLimits(int minutes)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new GameSettings { UpdateInterval = TimeSpan.FromMinutes(minutes) });

    [Fact]
    public void AGamesIntervalCanBeTheShortestOrTheLongest()
    {
        Assert.Equal(UpdateInterval.Minimum, new GameSettings { UpdateInterval = UpdateInterval.Minimum }.UpdateInterval);
        Assert.Equal(UpdateInterval.Maximum, new GameSettings { UpdateInterval = UpdateInterval.Maximum }.UpdateInterval);
    }

    [Fact]
    public void BrowseDefaultsAreBrowsesOwnChoices()
    {
        BrowseDefaults defaults = new()
        {
            Sort = SearchSort.Updated,
            Direction = SearchDirection.Ascending,
            UpdatedWithin = TimeSpan.FromDays(7),
            PageSize = 100,
        };

        Assert.Equal((SearchSort.Updated, SearchDirection.Ascending, TimeSpan.FromDays(7), 100), (defaults.Sort, defaults.Direction, defaults.UpdatedWithin, defaults.PageSize));
        Assert.False(defaults.IsNone);
        Assert.True(new BrowseDefaults().IsNone);
        Assert.Equal([TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30)], BrowseDefaults.Windows);
        Assert.Equal([20, 50, 100], BrowseDefaults.PageSizes);
    }

    [Fact]
    public void BrowseDefaultsRefuseWhatBrowseDoesntOffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseDefaults { Sort = (SearchSort)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseDefaults { Direction = (SearchDirection)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseDefaults { UpdatedWithin = TimeSpan.FromDays(3) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseDefaults { PageSize = 10 });
    }
}
