using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Settings;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One screen's previews, in General › Previews: rows, rows with thumbnails, or, where the screen offers them, cards.
/// A choice is saved as it's made.
/// </summary>
public sealed partial class ScreenPreviewsViewModel : ObservableObject
{
    private readonly Action<PreviewScreen, PreviewLevel> _choose;

    /// <param name="name">The screen's name, such as Library.</param>
    /// <param name="note">What the screen's previews cover, such as its games and a mod's page; null for nothing to add.</param>
    /// <param name="choose">Saves a choice the user made.</param>
    internal ScreenPreviewsViewModel(PreviewScreen screen, string name, string? note, Action<PreviewScreen, PreviewLevel> choose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(choose);

        Screen = screen;
        Name = name;
        Note = note;
        _choose = choose;
        OffersCards = screen is PreviewScreen.Library or PreviewScreen.Browse;
        Level = AppSettings.DefaultPreview(screen);
    }

    public PreviewScreen Screen { get; }

    public string Name { get; }

    public string? Note { get; }

    public bool HasNote => Note is not null;

    /// <summary>
    /// Whether the screen can show its mods as cards; Updates and Downloads can't.
    /// </summary>
    public bool OffersCards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRows), nameof(IsThumbnails), nameof(IsCards))]
    public partial PreviewLevel Level { get; private set; }

    public bool IsRows
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
    /// Shows the previews <paramref name="settings"/> store for the screen, without saving them back: thumbnails where
    /// they say cards and the screen has none.
    /// </summary>
    internal void Show(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        PreviewLevel level = settings.PreviewFor(Screen);
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
            _choose(Screen, level);
        }
    }
}
