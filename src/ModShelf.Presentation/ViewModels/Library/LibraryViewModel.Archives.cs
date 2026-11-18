using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Archives;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Import archives… (ADR 0101): another mod manager's downloads of a game, the game open or one chosen, identified and
/// copied into the library through the import dialog. The folder is only read.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>
    /// Asks for the game, unless one is open, and the folder of its archives, lists them, and shows the import dialog,
    /// which identifies and copies them; or says why the folder can't be imported from.
    /// </summary>
    [RelayCommand]
    private async Task ImportArchivesAsync(CancellationToken cancellationToken)
    {
        (GameRef Game, string Name)? chosen = OpenGame is { } open ? (open.Game, open.Name) : await ChooseArchivesGameAsync(cancellationToken);
        if (chosen is not { } game
            || await _services.Dialogs.PickFolderAsync(Text.Format(Strings.ArchivePickFolderTitle, game.Name), cancellationToken) is not { } folder)
        {
            return;
        }

        ArchiveScan scan;
        try
        {
            scan = await _archives.ScanAsync(folder, game.Game, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            string said = exception is ArgumentException argument ? argument.MessageForUser() : exception.Message;
            await _services.Dialogs.ShowMessageAsync(Strings.ArchiveScanFailedTitle, said, cancellationToken);
            return;
        }

        if (scan.Archives.Count == 0)
        {
            await _services.Dialogs.ShowMessageAsync(Strings.ArchiveNoneTitle, Text.Format(Strings.ArchiveNoneMessage, scan.Folder), cancellationToken);
            return;
        }

        await _services.Dialogs.ImportArchivesAsync(
            new ImportArchivesViewModel(_archives, scan, game.Name, _sources, _services.Dialogs, _timeProvider, _logger),
            cancellationToken);
    }

    /// <summary>
    /// Asks which game the archives are of, among every site's games, as Settings' Add a game… does.
    /// </summary>
    /// <returns>The game and its name; null when none was chosen.</returns>
    private async Task<(GameRef Game, string Name)?> ChooseArchivesGameAsync(CancellationToken cancellationToken)
    {
        AddGameViewModel choosing = new(_lookup, _sources, _pictures, [], _logger, Strings.ArchiveGameTitle, Strings.ArchiveGameMessage, Strings.ArchiveGameConfirm);
        choosing.StartReading(cancellationToken);
        if (!await _services.Dialogs.AddGameAsync(choosing, cancellationToken))
        {
            return null;
        }

        if (choosing.Game is not { } game)
        {
            await _services.Dialogs.ShowMessageAsync(Strings.ArchiveGameTitle, Strings.ArchiveGameByKey, cancellationToken);
            return null;
        }

        return (game, await GameNameAsync(game, cancellationToken));
    }

    /// <summary>
    /// The game's name: the library's, or its site's, which listed it for the choice; its key when neither says.
    /// </summary>
    private async Task<string> GameNameAsync(GameRef game, CancellationToken cancellationToken)
    {
        if (Games.FirstOrDefault(listed => listed.Game == game) is { } inLibrary)
        {
            return inLibrary.Name;
        }

        try
        {
            IReadOnlyList<GameInfo> games = await _lookup.GetGamesAsync(game.Source, cancellationToken);
            return games.FirstOrDefault(listed => listed.Game == game)?.Name ?? game.Key;
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            return game.Key;
        }
    }
}
