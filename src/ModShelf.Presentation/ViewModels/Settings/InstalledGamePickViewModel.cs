using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Installed;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The dialog choosing an installed game's games: its games on the sites as matched, each with <b>None here</b> and
/// <b>Match by name</b>, and the game picker over every site's games, a game chosen there becoming its game on its site.
/// Each choice is kept at once, and closes the dialog.
/// </summary>
public sealed partial class InstalledGamePickViewModel : ObservableObject
{
    private readonly InstalledGameMatch _match;
    private readonly IInstalledGameMatcher _matcher;
    private readonly ILogger _logger;

    /// <param name="listed">The sites' games read, which the picker offers; those of sites that find theirs by name are
    /// found as a name is typed.</param>
    internal InstalledGamePickViewModel(
        InstalledGameMatch match,
        IEnumerable<GameInfo> listed,
        IInstalledGameMatcher matcher,
        IModLookup lookup,
        ISourceRegistry sources,
        ModPictures pictures,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);

        _match = match;
        _matcher = matcher;
        _logger = logger;
        Sites =
        [
            .. match.Sites
                .Where(site => site.Picked || site.Games.Count > 0)
                .Select(site => new InstalledSiteViewModel(site, sources.SiteName(site.Site), None, ByName)),
        ];
        Picker = new GamePickerViewModel(
            pictures,
            sources,
            Choose,
            byName: new GamesByName(lookup, sources.All<IModCatalog>().Select(catalog => catalog.Id), logger));
        Picker.ShowGames(listed);
    }

    /// <summary>
    /// Asks to close the dialog, a choice kept.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Such as "The Elder Scrolls V: Skyrim Special Edition: choose its game on a site, below, or say a site has none."
    /// </summary>
    public string Message => Text.Format(Strings.InstalledPickMessage, _match.Game.Name);

    /// <summary>
    /// Its games on the sites, by name or chosen.
    /// </summary>
    public IReadOnlyList<InstalledSiteViewModel> Sites { get; }

    public bool HasSites => Sites.Count > 0;

    public GamePickerViewModel Picker { get; }

    /// <summary>
    /// Why the choice couldn't be kept; null when it was, or before one.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Keeping the last choice, for the caller and tests to wait on.
    /// </summary>
    internal Task Changing { get; private set; } = Task.CompletedTask;

    private void Choose(GameRef game) => Change(() => _matcher.PickAsync(_match.Game, game.Source, game, CancellationToken.None));

    private void None(SourceId site) => Change(() => _matcher.PickAsync(_match.Game, site, null, CancellationToken.None));

    private void ByName(SourceId site) => Change(() => _matcher.UnpickAsync(_match.Game, site, CancellationToken.None));

    private void Change(Func<Task> change) => Changing = ChangeAsync(change);

    private async Task ChangeAsync(Func<Task> change)
    {
        try
        {
            await change();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            LogNotKept(_logger, exception);
            Problem = Text.Format(Strings.InstalledPickNotKept, exception.Message);
            return;
        }

        Problem = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
