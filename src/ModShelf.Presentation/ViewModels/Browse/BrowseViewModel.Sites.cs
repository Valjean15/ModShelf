using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The site box: Browse's games narrowed to one site's, remembered between runs.
/// </summary>
public sealed partial class BrowseViewModel
{
    // Whether the site shown is put back as it was, rather than chosen: it isn't remembered again.
    private bool _showingSite;

    /// <summary>
    /// All sites, then each site that can search, by name.
    /// </summary>
    public IReadOnlyList<SiteChoice> Sites { get; }

    /// <summary>
    /// Whether the site box shows: once two sites or more can search.
    /// </summary>
    public bool HasSites => Sites.Count > 2;

    /// <summary>
    /// The site whose games the game picker offers, or all of them. Choosing a site while a game of another is chosen
    /// chooses the site's game the library has the most mods of, or else opens the picker to choose one.
    /// </summary>
    [ObservableProperty]
    public partial SiteChoice SelectedSite { get; set; }

    /// <summary>
    /// The site chosen last being remembered, for tests to wait on.
    /// </summary>
    internal Task RememberingSite { get; private set; } = Task.CompletedTask;

    partial void OnSelectedSiteChanged(SiteChoice value)
    {
        Picker.ShowSite(value.Source);
        if (Game is null || IsGamesProblem)
        {
            // Only the problems of the sites whose games are offered.
            ShowGamesProblem();
        }

        if (_showingSite)
        {
            return;
        }

        RememberingSite = RememberSiteAsync(value.Source);
        if (value.Source is { } site && Game is { } chosen && chosen.Game.Source != site)
        {
            if (Favourite([.. Games.Where(game => game.Game.Source == site)]) is { } favourite)
            {
                Game = favourite;
            }
            else
            {
                Picker.ClearCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="game"/> is of the site chosen, or every site is.
    /// </summary>
    private bool OfSiteChosen(GameRef game) => OfSiteChosen(game.Source);

    private bool OfSiteChosen(SourceId source) => SelectedSite.Source is not { } site || source == site;

    /// <summary>
    /// Shows <paramref name="choice"/> in the site box without choosing a game or remembering it, as when it's put back
    /// as it was.
    /// </summary>
    private void ShowSite(SiteChoice choice)
    {
        _showingSite = true;
        try
        {
            SelectedSite = choice;
        }
        finally
        {
            _showingSite = false;
        }
    }

    /// <summary>
    /// The box's choice for <paramref name="source"/>; all sites for a site it doesn't offer.
    /// </summary>
    private SiteChoice SiteChoiceOf(string? source)
        => Sites.FirstOrDefault(choice => choice.Source is { } site && string.Equals(site.Value, source, StringComparison.Ordinal)) ?? Sites[0];

    /// <summary>
    /// Remembers the site chosen, for the next run. One that can't be remembered is logged.
    /// </summary>
    private async Task RememberSiteAsync(SourceId? site)
    {
        try
        {
            await _uiState.ChangeAsync(state => state with { BrowseSite = site?.Value }, _stopping);
        }
        catch (IOException exception)
        {
            LogSiteNotRemembered(_logger, exception);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing.
        }
    }
}
