using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The games installed on this computer, first in the game picker by their games on the sites, once the sites' games are
/// read, and each time Browse shows again.
/// </summary>
public sealed partial class BrowseViewModel
{
    /// <summary>
    /// The installed games as last matched; null before.
    /// </summary>
    private InstalledGamesMatched? _installed;

    /// <summary>
    /// The last matching of the installed games, or picking one's game, for tests to wait on.
    /// </summary>
    internal Task InstalledWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Matches the installed games with the sites' games read, and offers them. When that fails, which is logged, the
    /// picker offers what it did.
    /// </summary>
    private async Task ShowInstalledAsync()
    {
        try
        {
            _installed = await _installedGames.MatchAsync([.. _allGames.Select(game => game.Info)], _stopping);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogInstalledUnread(_logger, exception);
            return;
        }

        ShowInstalled();
    }

    /// <summary>
    /// Offers the installed games' games, but those the settings hide.
    /// </summary>
    private void ShowInstalled()
    {
        if (_installed is { } installed)
        {
            Picker.ShowInstalled(installed.Stores, installed.Games.Select(match => match with
            {
                Sites = [.. match.Sites.Select(site => site with { Games = [.. site.Games.Where(game => !_hiddenGames.IsHidden(game.Game))] })],
            }));
        }
    }

    /// <summary>
    /// Remembers the game chosen as an installed game's, of two or more its name matched, then matches the installed
    /// games again.
    /// </summary>
    private void PickInstalled(InstalledGame installed, GameRef game) => InstalledWork = PickInstalledAsync(installed, game);

    private async Task PickInstalledAsync(InstalledGame installed, GameRef game)
    {
        try
        {
            await _installedGames.PickAsync(installed, game.Source, game, _stopping);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            LogInstalledNotPicked(_logger, exception, game.Source.Value);
        }

        await ShowInstalledAsync();
    }
}
