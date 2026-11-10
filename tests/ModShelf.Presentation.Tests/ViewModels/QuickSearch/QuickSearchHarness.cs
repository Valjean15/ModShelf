using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.QuickSearch;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.QuickSearch;

/// <summary>
/// Quick search over fakes: Nexus Mods lists Skyrim Special Edition, with its cover, Stardew Valley and Fallout 4; the
/// settings have a part on where files go and one on the theme.
/// </summary>
internal sealed class QuickSearchHarness
{
    public static readonly GameRef Skyrim = new(TestData.Nexus, "skyrimspecialedition");

    public static readonly GameRef Stardew = new(TestData.Nexus, "stardewvalley");

    public static readonly GameRef Fallout = new(TestData.Nexus, "fallout4");

    public static readonly Uri SkyrimCover = new("https://images.nexusmods.com/images/games/v2/1704/skyrim-hero.jpg");

    /// <summary>
    /// A site that finds its games by name, as GameBanana does.
    /// </summary>
    public static readonly SourceId Banana = new("gamebanana");

    public static readonly GameRef Royal = new(Banana, "16951");

    public static readonly GameRef Persona = new(Banana, "7545");

    /// <param name="banana">Whether <see cref="Banana"/> is a site too.</param>
    public QuickSearchHarness(NavigationService? navigation = null, bool banana = false)
    {
        Navigation = navigation ?? new NavigationService();
        Screens = new ScreenOpener(Navigation);
        Lookup.Searchable.Add(TestData.Nexus);
        Lookup.Games[TestData.Nexus] =
        [
            new GameInfo(Skyrim, "Skyrim Special Edition", coverUrl: SkyrimCover),
            new GameInfo(Stardew, "Stardew Valley"),
            new GameInfo(Fallout, "Fallout 4"),
        ];
        ScriptedCatalog bananaCatalog = new(Banana, "GameBanana");
        if (banana)
        {
            Lookup.Searchable.Add(Banana);
            Lookup.ByName.Add(Banana);
            Lookup.Games[Banana] = [new GameInfo(Royal, "Persona 5 Royal (PC)"), new GameInfo(Persona, "Persona 5")];
            bananaCatalog.GameNames[Royal.Key] = "Persona 5 Royal (PC)";
        }

        Settings.Listed.Add(new SettingsSearchEntry(SettingsCategory.Library, "Library", "Where files go", "Where files go Downloads are saved in the library's folder"));
        Settings.Listed.Add(new SettingsSearchEntry(SettingsCategory.General, "General", "Theme", "Theme Light Dark System"));
        Search = new QuickSearchViewModel(
            Library,
            Lookup,
            new FakeSourceRegistry(new ScriptedCatalog(TestData.Nexus, "Nexus Mods"), bananaCatalog),
            Hidden,
            Settings,
            Screens,
            GameSettings,
            Navigation,
            UiState,
            Pictures.Pictures,
            Logger);
    }

    public InMemoryModLibrary Library { get; } = new();

    public ScriptedLookup Lookup { get; } = new();

    public HiddenGames Hidden { get; } = new();

    public FixedSettingsIndex Settings { get; } = new();

    public NavigationService Navigation { get; }

    public ScreenOpener Screens { get; }

    public RecordingGameSettingsOpener GameSettings { get; } = new();

    public MemoryUiState UiState { get; } = new();

    public TestPictures Pictures { get; } = new();

    public RecordingLogger<QuickSearchViewModel> Logger { get; } = new();

    public QuickSearchViewModel Search { get; }

    /// <summary>
    /// Adds <paramref name="mods"/> to the library, opens quick search and waits until it has read the library and the
    /// sites' games.
    /// </summary>
    public async Task OpenAsync(params LibraryMod[] mods)
    {
        Library.Add(mods);
        Search.OpenCommand.Execute(null);
        await Search.Reading;
    }

    /// <summary>
    /// Types <paramref name="text"/> and returns the rows found as <see cref="Lines"/> does.
    /// </summary>
    public IReadOnlyList<string> Type(string text)
    {
        Search.Query = text;
        return Lines();
    }

    /// <summary>
    /// The rows as text: "# " and the heading before each group's first, then each row's name with its line about it
    /// after " — ", and "&gt; " before the row highlighted.
    /// </summary>
    public IReadOnlyList<string> Lines()
    {
        List<string> lines = [];
        foreach (QuickSearchEntryViewModel entry in Search.Entries)
        {
            if (entry.Heading is { } heading)
            {
                lines.Add($"# {heading}");
            }

            lines.Add($"{(entry.IsHighlighted ? "> " : string.Empty)}{entry.Name}{(entry.Meta is { } meta ? $" — {meta}" : string.Empty)}");
        }

        return lines;
    }
}
