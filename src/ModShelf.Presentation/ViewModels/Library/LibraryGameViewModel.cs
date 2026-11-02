using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// A game on the Library screen: its tile or row among the games, with how many mods it has, the updates found for
/// them, the space their files take and when they were last checked, and the heading of its mods once it's open.
/// </summary>
public sealed partial class LibraryGameViewModel : ObservableObject
{
    private readonly Action<LibraryGameViewModel> _open;
    private readonly TimeProvider _timeProvider;

    /// <param name="open">Opens the game, showing its mods in place of the games.</param>
    internal LibraryGameViewModel(GameRef game, Action<LibraryGameViewModel> open, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Game = game;
        _open = open;
        _timeProvider = timeProvider;
        CoverHue = HueOf(game.Key);
        Name = game.Key;
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
    public string Initial => Name.Length > 0 ? char.ToUpper(Name[0], System.Globalization.CultureInfo.CurrentCulture).ToString() : string.Empty;

    /// <summary>
    /// The hue of its cover, from 0 to 359, the same for the game every time.
    /// </summary>
    public int CoverHue { get; }

    /// <summary>
    /// How many mods it has in the library.
    /// </summary>
    public int ModCount { get; private set; }

    /// <summary>
    /// Such as "12 mods".
    /// </summary>
    [ObservableProperty]
    public partial string ModsText { get; private set; } = string.Empty;

    /// <summary>
    /// How many updates were found for its mods.
    /// </summary>
    public int UpdateCount { get; private set; }

    /// <summary>
    /// Such as "2 updates", or "Up to date".
    /// </summary>
    [ObservableProperty]
    public partial string UpdatesText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUpdates { get; private set; }

    /// <summary>
    /// The space its mods' files take, in bytes, archived versions included.
    /// </summary>
    public long Size { get; private set; }

    /// <summary>
    /// Such as "18.4 GB".
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "1.2 GB archived"; empty without archived versions.
    /// </summary>
    [ObservableProperty]
    public partial string ArchivedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasArchived { get; private set; }

    /// <summary>
    /// Such as "2 hours ago": when its mods were last checked for updates; "Not yet" when they never were.
    /// </summary>
    [ObservableProperty]
    public partial string CheckedText { get; private set; } = string.Empty;

    /// <summary>
    /// What its heading says once it's open, such as "73 mods · 2 updates · 18.4 GB on disk, 1.2 GB of it archived
    /// versions · checked 2 hours ago".
    /// </summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Open Stardew Valley", for screen readers.
    /// </summary>
    public string OpenText => Text.Format(Strings.LibraryOpenGame, Name);

    /// <summary>
    /// When its mods were last checked for updates; null when they never were.
    /// </summary>
    internal DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>
    /// Shows what its mods add up to now. Runs on the UI thread.
    /// </summary>
    internal void Show(IReadOnlyCollection<LibraryModViewModel> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        ModCount = mods.Count;
        ModsText = Counts.Of(mods.Count, Strings.LibraryOneMod, Strings.LibraryMods);
        UpdateCount = mods.Sum(mod => mod.UpdateCount);
        UpdatesText = Counts.Of(UpdateCount, Strings.LibraryUpToDate, Strings.LibraryOneUpdate, Strings.LibraryUpdates);
        HasUpdates = UpdateCount > 0;
        Size = mods.Sum(mod => mod.Size);
        SizeText = Sizes.Format(Size);
        long archived = mods.Sum(mod => mod.ArchivedSize);
        HasArchived = archived > 0;
        ArchivedText = HasArchived ? Text.Format(Strings.LibrarySizeArchived, Sizes.Format(archived)) : string.Empty;
        CheckedAt = mods.Max(mod => mod.CheckedAt);
        CheckedText = CheckedAt is { } checkedAt ? Dates.Ago(checkedAt, _timeProvider) : Strings.LibraryNotChecked;
        string summary = HasArchived
            ? Text.Format(Strings.LibraryGameSummaryArchived, ModsText, UpdatesText, SizeText, Sizes.Format(archived))
            : Text.Format(Strings.LibraryGameSummary, ModsText, UpdatesText, SizeText);
        SummaryText = CheckedAt is { } checkedSummary ? Text.Format(Strings.LibraryCheckedAgo, summary, Dates.Ago(checkedSummary, _timeProvider)) : summary;
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(OpenText));

    /// <summary>
    /// A hue from the game's key that's the same in every run, which <see cref="string.GetHashCode()"/> isn't.
    /// </summary>
    private static int HueOf(string key)
    {
        uint hash = 2166136261;
        foreach (char letter in key)
        {
            hash = (hash ^ letter) * 16777619;
        }

        return (int)(hash % 360);
    }

    /// <summary>
    /// Shows the game's mods in place of the games.
    /// </summary>
    [RelayCommand]
    private void Open() => _open(this);
}
