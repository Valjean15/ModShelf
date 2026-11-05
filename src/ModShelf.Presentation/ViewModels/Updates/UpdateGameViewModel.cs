using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Updates;

/// <summary>
/// A game on the Updates screen: its heading, with how many updates its mods have and Update all, then its updates.
/// </summary>
public sealed partial class UpdateGameViewModel : ObservableObject
{
    private readonly Func<UpdateGameViewModel, CancellationToken, Task> _updateAll;

    /// <param name="siteName">The game's site's name, such as Nexus Mods.</param>
    /// <param name="updateAll">Downloads the game's updates into the library, one after the other.</param>
    internal UpdateGameViewModel(GameRef game, string siteName, Func<UpdateGameViewModel, CancellationToken, Task> updateAll, ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);
        ArgumentNullException.ThrowIfNull(updateAll);

        Game = game;
        SiteName = siteName;
        _updateAll = updateAll;
        CoverHue = Covers.HueOf(game.Key);
        Name = game.Key;
        Icon = new GamePictureViewModel(pictures);
    }

    public GameRef Game { get; }

    /// <summary>
    /// The game's name on its site; its key, such as skyrimspecialedition, until the site says.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    public partial string Name { get; internal set; }

    /// <summary>
    /// The first letter of its name, on its cover until the game has a picture of its own.
    /// </summary>
    public string Initial => Covers.InitialOf(Name);

    /// <summary>
    /// The game's site's name, such as Nexus Mods, on its chip.
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Whether its site shows, as a chip: once two sites are in use.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSite { get; internal set; }

    /// <summary>
    /// The hue of its cover, from 0 to 359, the same for the game every time.
    /// </summary>
    public int CoverHue { get; }

    /// <summary>
    /// Its small square picture on its site, on its cover in place of its initial.
    /// </summary>
    public GamePictureViewModel Icon { get; }

    /// <summary>
    /// Its mods' updates, by mod name, then the newest first.
    /// </summary>
    public ObservableCollection<UpdateViewModel> Updates { get; } = [];

    /// <summary>
    /// Such as "2 updates".
    /// </summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Update all (2)": how many of its updates can be downloaded.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateAllText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether any of its updates can be downloaded, while the screen isn't downloading others.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    public partial bool CanUpdateAll { get; private set; }

    /// <summary>
    /// Shows its updates now, and whether Update all can run. Runs on the UI thread.
    /// </summary>
    /// <param name="busy">Whether the screen is downloading updates already.</param>
    internal void Show(IReadOnlyList<UpdateViewModel> updates, bool busy)
    {
        ArgumentNullException.ThrowIfNull(updates);

        Updates.SyncWith(updates);
        CountText = Counts.Of(updates.Count, Strings.LibraryOneUpdate, Strings.LibraryUpdates);
        int downloadable = updates.Count(update => update.CanDownload);
        UpdateAllText = Text.Format(Strings.UpdatesUpdateAll, downloadable);
        CanUpdateAll = downloadable > 0 && !busy;
    }

    /// <summary>
    /// Downloads each of its updates into the library, as Download does for one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private Task UpdateAllAsync(CancellationToken cancellationToken) => _updateAll(this, cancellationToken);
}
