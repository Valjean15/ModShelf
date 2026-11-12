using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// An installed game in Settings › Games: its name, its games on the sites, and <b>Choose…</b>.
/// </summary>
public sealed partial class InstalledGameRowViewModel : ObservableObject
{
    private readonly ISourceRegistry _sources;
    private readonly Func<InstalledGameRowViewModel, Task> _choose;

    internal InstalledGameRowViewModel(InstalledGameMatch match, ISourceRegistry sources, Func<InstalledGameRowViewModel, Task> choose)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(choose);

        _sources = sources;
        _choose = choose;
        Game = match.Game;
        Match = match;
        Show(match);
    }

    public InstalledGame Game { get; }

    public string Name => Game.Name;

    /// <summary>
    /// Such as "Choose The Elder Scrolls V: Skyrim Special Edition's games", for a screen reader.
    /// </summary>
    public string ChooseText => Text.Format(Strings.InstalledRowChooseName, Game.Name);

    /// <summary>
    /// The game's games on the sites, as last matched.
    /// </summary>
    public InstalledGameMatch Match { get; private set; }

    /// <summary>
    /// Such as "Nexus Mods: Skyrim Special Edition, chosen · mod.io: none, chosen", or "No site's game yet".
    /// </summary>
    [ObservableProperty]
    public partial string MatchesText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether a site has a game for it, by name or chosen.
    /// </summary>
    [ObservableProperty]
    public partial bool HasGame { get; private set; }

    /// <summary>
    /// Shows the game's games as matched again.
    /// </summary>
    internal void Show(InstalledGameMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        Match = match;
        string[] sites = [.. match.Sites.Where(site => site.Picked || site.Games.Count > 0).Select(Describe)];
        MatchesText = sites.Length == 0 ? Strings.InstalledRowNoGame : string.Join(" · ", sites);
        HasGame = match.Sites.Any(site => site.Games.Count > 0);
    }

    [RelayCommand]
    private Task ChooseAsync() => _choose(this);

    private string Describe(SiteMatch site)
    {
        string name = _sources.SiteName(site.Site);
        return (site.Picked, site.Games.Count) switch
        {
            (true, 0) => Text.Format(Strings.InstalledRowNoneChosen, name),
            (true, _) => Text.Format(Strings.InstalledRowChosen, name, site.Games[0].Name),
            (false, 1) => Text.Format(Strings.InstalledRowGame, name, site.Games[0].Name),
            _ => Text.Format(Strings.InstalledRowShared, name, site.Games.Count),
        };
    }
}
