using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The screen's categories, one shown at a time, and the search, which shows the settings that match from every
/// category instead.
/// </summary>
public sealed partial class SettingsViewModel
{
    private static readonly char[] Spaces = [' ', '\t'];

    /// <summary>
    /// General, Library, Updates, Links, Games and About.
    /// </summary>
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; } =
    [
        new(SettingsCategory.General, Strings.SettingsCategoryGeneral),
        new(SettingsCategory.Library, Strings.SettingsCategoryLibrary),
        new(SettingsCategory.Updates, Strings.SettingsCategoryUpdates),
        new(SettingsCategory.Links, Strings.SettingsCategoryLinks),
        new(SettingsCategory.Games, Strings.SettingsCategoryGames),
        new(SettingsCategory.About, Strings.SettingsCategoryAbout),
    ];

    /// <summary>
    /// The category shown, while no search is under way; General at first.
    /// </summary>
    [ObservableProperty]
    public partial SettingsCategoryViewModel? SelectedCategory { get; set; }

    /// <summary>
    /// Words to look for in the settings' headings, explanations and choices; empty for none, which shows the category
    /// chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Whether the settings shown are a search's, from every category.
    /// </summary>
    public bool IsSearching => Words().Length > 0;

    /// <summary>
    /// Such as "Settings that match “theme”"; empty when no search is under way.
    /// </summary>
    [ObservableProperty]
    public partial string SearchHeading { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "No setting matches “zzz”."; null unless a search found nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? NothingMatches { get; private set; }

    /// <summary>
    /// The theme: General.
    /// </summary>
    public SettingsSectionViewModel ThemeSection { get; } = new(
        SettingsCategory.General,
        Strings.SettingsAppearanceHeading,
        Strings.SettingsThemeSystem,
        Strings.SettingsThemeLight,
        Strings.SettingsThemeDark);

    /// <summary>
    /// What closing the window does: General.
    /// </summary>
    public SettingsSectionViewModel WindowSection { get; } = new(
        SettingsCategory.General,
        Strings.SettingsWindowHeading,
        Strings.SettingsCloseAsk,
        Strings.SettingsCloseToTray,
        Strings.SettingsCloseQuit);

    /// <summary>
    /// Saved pictures, mod details and adult content: General.
    /// </summary>
    public SettingsSectionViewModel PicturesSection { get; } = new(
        SettingsCategory.General,
        Strings.SettingsPicturesHeading,
        Strings.SettingsSavedPictures,
        Strings.SettingsPicturesClear,
        Strings.SettingsModDetails,
        Strings.SettingsModDetailsNote,
        Strings.SettingsModDetailsReadAgain,
        Strings.SettingsAdultContent,
        Strings.SettingsAdultContentNote,
        Strings.AdultPicturesHidden,
        Strings.AdultPicturesBlurred,
        Strings.AdultPicturesShown);

    /// <summary>
    /// Where the library is: Library.
    /// </summary>
    public SettingsSectionViewModel LibraryFolderSection { get; } = new(
        SettingsCategory.Library,
        Strings.SettingsLibraryHeading,
        Strings.SettingsChooseFolder,
        Strings.SettingsUseDefaultFolder);

    /// <summary>
    /// The folder pattern of every game without its own: Library.
    /// </summary>
    public SettingsSectionViewModel FolderPatternSection { get; } = new(
        SettingsCategory.Library,
        Strings.SettingsFolderPatternHeading,
        Strings.SettingsFoldersHeading,
        Strings.SettingsFoldersNote);

    /// <summary>
    /// What happens to old versions: Library.
    /// </summary>
    public SettingsSectionViewModel OldVersionsSection { get; } = new(
        SettingsCategory.Library,
        Strings.SettingsOldVersionsHeading,
        Strings.SettingsOldVersionsNote,
        Strings.SettingsKeepAll,
        Strings.SettingsArchive,
        Strings.SettingsKeepLatest);

    /// <summary>
    /// Rescanning the library: Library.
    /// </summary>
    public SettingsSectionViewModel RescanSection { get; } = new(
        SettingsCategory.Library,
        Strings.SettingsRescanHeading,
        Strings.SettingsRescanNote,
        Strings.SettingsRescanButton);

    /// <summary>
    /// How often updates are checked, and notices: Updates.
    /// </summary>
    public SettingsSectionViewModel UpdatesSection { get; } = new(
        SettingsCategory.Updates,
        Strings.SettingsUpdatesHeading,
        Strings.SettingsInterval,
        Strings.SettingsNotify);

    /// <summary>
    /// Which apps open the links websites hand downloads over with: Links.
    /// </summary>
    public SettingsSectionViewModel LinksSection { get; } = new(
        SettingsCategory.Links,
        Strings.LinksHeading,
        Strings.LinksExplain,
        Strings.LinksTake);

    /// <summary>
    /// Single games' folder patterns: Games.
    /// </summary>
    public SettingsSectionViewModel GamesSection { get; } = new(
        SettingsCategory.Games,
        Strings.SettingsGamesHeading,
        Strings.SettingsGamesNote,
        Strings.SettingsAddGame);

    /// <summary>
    /// This ModShelf, its updates, its diagnostics and where its settings are: About.
    /// </summary>
    public SettingsSectionViewModel AboutSection { get; } = new(
        SettingsCategory.About,
        Strings.AboutHeading,
        Strings.AppUpdateCheck,
        Strings.AppUpdateCheckAtStart,
        Strings.AboutBugReport,
        Strings.AboutCopyDiagnostics,
        Strings.AboutOpenLogFolder);

    private IEnumerable<SettingsSectionViewModel> Sections =>
    [
        ThemeSection,
        WindowSection,
        PicturesSection,
        LibraryFolderSection,
        FolderPatternSection,
        OldVersionsSection,
        RescanSection,
        UpdatesSection,
        LinksSection,
        GamesSection,
        AboutSection,
    ];

    /// <summary>
    /// Shows the category chosen, ending a search.
    /// </summary>
    partial void OnSelectedCategoryChanged(SettingsCategoryViewModel? value)
    {
        if (IsSearching)
        {
            SearchText = string.Empty;
        }
        else
        {
            ShowSections();
        }
    }

    partial void OnSearchTextChanged(string value) => ShowSections();

    /// <summary>
    /// Clears the search, back to the category chosen.
    /// </summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>
    /// Shows the chosen category's sections; or, while searching, those that say every word searched for, from every
    /// category, and which categories hold some.
    /// </summary>
    private void ShowSections()
    {
        string[] words = Words();
        if (words.Length == 0)
        {
            SettingsCategory category = (SelectedCategory ?? Categories[0]).Category;
            foreach (SettingsSectionViewModel section in Sections)
            {
                section.IsShown = section.Category == category;
            }

            foreach (SettingsCategoryViewModel shown in Categories)
            {
                shown.HasMatches = true;
            }

            SearchHeading = string.Empty;
            NothingMatches = null;
            return;
        }

        foreach (SettingsSectionViewModel section in Sections)
        {
            section.IsShown = section.Matches(words);
        }

        foreach (SettingsCategoryViewModel shown in Categories)
        {
            shown.HasMatches = Sections.Any(section => section.IsShown && section.Category == shown.Category);
        }

        string searched = SearchText.Trim();
        SearchHeading = Text.Format(Strings.SettingsMatches, searched);
        NothingMatches = Sections.Any(section => section.IsShown) ? null : Text.Format(Strings.SettingsNothingMatches, searched);
    }

    private string[] Words() => SearchText.Split(Spaces, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
