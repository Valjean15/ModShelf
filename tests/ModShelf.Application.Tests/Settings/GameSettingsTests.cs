using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Settings;

/// <summary>
/// A game's own settings, Browse's defaults, and what every game's settings give a game.
/// </summary>
public sealed class GameSettingsTests
{
    private static readonly FolderPattern Flat = FolderPattern.Parse("{mod}/{file}");

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

        Assert.Same(GameSettings.None, settings.GameFor("stardewvalley"));
        Assert.Equal(
            (Flat, OldVersionRule.Archive, TimeSpan.FromHours(1), false, true, false),
            (settings.FoldersFor("stardewvalley"), settings.OldVersionsFor("stardewvalley"), settings.IntervalFor("stardewvalley"),
                settings.NotifiesFor("stardewvalley"), settings.ChecksUpdatesOf("stardewvalley"), settings.IsHidden("stardewvalley")));
    }

    [Fact]
    public void EveryGamesSettingsLeftOutAreTheirDefaults()
    {
        AppSettings settings = new();

        Assert.Equal(
            (FolderPattern.Default, OldVersionRule.Default, UpdateInterval.Default, true, true, false),
            (settings.FoldersFor("x"), settings.OldVersionsFor("x"), settings.IntervalFor("x"), settings.NotifiesFor("x"), settings.ChecksUpdatesOf("x"),
                settings.IsHidden("x")));
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
            Games = new Dictionary<string, GameSettings>(StringComparer.OrdinalIgnoreCase) { ["StardewValley"] = own },
        };

        Assert.Same(own, settings.GameFor("stardewvalley"));
        Assert.Equal(
            (Flat, OldVersionRule.KeepAll, TimeSpan.FromDays(1), true, false, true),
            (settings.FoldersFor("STARDEWVALLEY"), settings.OldVersionsFor("stardewvalley"), settings.IntervalFor("stardewvalley"),
                settings.NotifiesFor("stardewvalley"), settings.ChecksUpdatesOf("stardewvalley"), settings.IsHidden("stardewvalley")));
        Assert.Equal((OldVersionRule.Archive, true), (settings.OldVersionsFor("skyrimspecialedition"), settings.ChecksUpdatesOf("skyrimspecialedition")));
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
