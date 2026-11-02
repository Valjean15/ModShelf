using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Settings;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// The previews a screen shows its mods with: none, thumbnails beside them, or, where the screen offers them, cards with
/// large pictures. A choice is saved, and the settings' is shown as the screen starts.
/// </summary>
public sealed partial class PreviewsViewModel : ObservableObject
{
    private readonly PreviewScreen _screen;
    private readonly ModPictures _pictures;

    internal PreviewsViewModel(PreviewScreen screen, ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);

        _screen = screen;
        _pictures = pictures;
        OffersCards = screen is PreviewScreen.Library or PreviewScreen.Browse;
        Level = AppSettings.DefaultPreview(screen);
    }

    /// <summary>
    /// Whether the screen can show its mods as cards; Updates and Downloads can't.
    /// </summary>
    public bool OffersCards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff), nameof(IsThumbnails), nameof(IsCards), nameof(ShowsPictures), nameof(ShowsRows))]
    public partial PreviewLevel Level { get; private set; }

    public bool IsOff
    {
        get => Level == PreviewLevel.Off;
        set => Choose(value, PreviewLevel.Off);
    }

    public bool IsThumbnails
    {
        get => Level == PreviewLevel.Thumbnails;
        set => Choose(value, PreviewLevel.Thumbnails);
    }

    public bool IsCards
    {
        get => Level == PreviewLevel.Cards;
        set => Choose(value, PreviewLevel.Cards);
    }

    /// <summary>
    /// Whether the mods show pictures, as thumbnails or cards.
    /// </summary>
    public bool ShowsPictures => Level != PreviewLevel.Off;

    /// <summary>
    /// Whether the mods show as rows rather than cards.
    /// </summary>
    public bool ShowsRows => Level != PreviewLevel.Cards;

    /// <summary>
    /// Shows the previews the settings say, as the screen starts or after another screen changed them.
    /// </summary>
    internal void Refresh()
    {
        PreviewLevel level = _pictures.LevelOf(_screen);
        Level = level == PreviewLevel.Cards && !OffersCards ? PreviewLevel.Thumbnails : level;
    }

    /// <summary>
    /// Makes a choice, from one of the buttons that offer it; unchecking one does nothing.
    /// </summary>
    private void Choose(bool chosen, PreviewLevel level)
    {
        if (chosen && level != Level && (level != PreviewLevel.Cards || OffersCards))
        {
            Level = level;
            _ = _pictures.SetLevelAsync(_screen, level);
        }
    }
}
