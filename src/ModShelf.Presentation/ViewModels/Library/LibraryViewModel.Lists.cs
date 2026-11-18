using System.Net;

using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Lists;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Export list… and Import list… (ADR 0098): the library, the game open or the mods selected written to a list file,
/// and a list file, or a site's collection by its link, brought into the library through the import dialog.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>
    /// The largest list file read, far more than a library of thousands of mods takes.
    /// </summary>
    private const long MaxListBytes = 64L * 1024 * 1024;

    /// <summary>
    /// A name for a list file made of a game's key, with what isn't a letter or a digit as a hyphen, so it's a name on
    /// every system.
    /// </summary>
    internal static string ListFileName(GameRef? game)
    {
        string name = game is null ? "library" : string.Concat(game.Key.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')).Trim('-');
        return $"{(name.Length == 0 ? "game" : name)}.modshelf-list.json";
    }

    /// <summary>
    /// Writes a list of the library's mods to a file the user chooses: the selected mods of the game open, when some are;
    /// otherwise the game open's; otherwise the whole library's.
    /// </summary>
    [RelayCommand]
    private async Task ExportListAsync(CancellationToken cancellationToken)
    {
        LibraryGameViewModel? game = OpenGame;
        ModRef[] selected = game is null ? [] : [.. GameMods.Where(mod => mod.IsSelected).Select(mod => mod.Mod)];
        ListScope scope = game is null ? ListScope.Library : new ListScope(game.Game, selected.Length > 0 ? selected : null);
        string title = selected.Length > 0 ? Strings.ListExportSelectedTitle
            : game is null ? Strings.ListExportTitle
            : Text.Format(Strings.ListExportGameTitle, game.Name);
        if (await _services.Dialogs.PickSaveFileAsync(title, ListFileName(game?.Game), cancellationToken) is not { } path)
        {
            return;
        }

        try
        {
            ModList list = await _lists.ExportAsync(scope, cancellationToken);
            await File.WriteAllTextAsync(path, _lists.Write(list), cancellationToken);
            string said = list.Mods.Count == 1 ? Text.Format(Strings.ListExportedOne, path) : Text.Format(Strings.ListExported, list.Mods.Count, path);
            await _services.Dialogs.ShowMessageAsync(Strings.ListExportedTitle, said, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _services.Dialogs.ShowMessageAsync(Strings.ListExportFailedTitle, exception.Message, cancellationToken);
        }
    }

    /// <summary>
    /// Reads a list file the user chooses and shows what importing it would do, in its dialog, which imports it.
    /// </summary>
    [RelayCommand]
    private async Task ImportListAsync(CancellationToken cancellationToken)
    {
        if (await _services.Dialogs.PickFileAsync(Strings.ListImportPickTitle, null, cancellationToken) is not { } path)
        {
            return;
        }

        ListImportPlan plan;
        try
        {
            if (new FileInfo(path).Length > MaxListBytes)
            {
                throw new InvalidDataException(Strings.ListTooLarge);
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken);
            plan = await _lists.PlanAsync(_lists.Read(text), cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await _services.Dialogs.ShowMessageAsync(Strings.ListUnreadableTitle, exception.Message, cancellationToken);
            return;
        }

        await ShowImportAsync(plan, cancellationToken);
    }

    /// <summary>
    /// Asks for a collection's link, reads the collection on its site, and shows what importing it would do, in the
    /// import dialog, which imports it; or says why it couldn't be read.
    /// </summary>
    [RelayCommand]
    private async Task ImportCollectionAsync(CancellationToken cancellationToken)
    {
        TextQuestion question = new(Strings.CollectionAskTitle, Strings.CollectionAskMessage, Strings.CollectionAskHint, Strings.CollectionAskConfirm);
        if (await _services.Dialogs.AskTextAsync(question, cancellationToken) is not { } answer || string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        ModList? list = null;
        string? problem;
        if (!Uri.TryCreate(answer.Trim(), UriKind.Absolute, out Uri? link) || link.IsFile || link.IsUnc)
        {
            problem = Strings.CollectionNotALink;
        }
        else
        {
            try
            {
                list = await _lists.ReadCollectionAsync(link, cancellationToken);
                problem = list is null ? Strings.CollectionNotOne : null;
            }
            catch (ArgumentException exception)
            {
                problem = exception.MessageForUser();
            }
            catch (AuthenticationFailedException)
            {
                problem = Strings.CollectionNeedsAccount;
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                problem = Strings.CollectionNotFound;
            }
            catch (HttpRequestException exception)
            {
                problem = Text.Format(Strings.CollectionFailed, exception.Message);
            }
        }

        if (list is null)
        {
            await _services.Dialogs.ShowMessageAsync(Strings.CollectionUnreadableTitle, problem ?? Strings.CollectionNotOne, cancellationToken);
            return;
        }

        await ShowImportAsync(await _lists.PlanAsync(list, cancellationToken), cancellationToken);
    }

    private Task ShowImportAsync(ListImportPlan plan, CancellationToken cancellationToken)
        => _services.Dialogs.ImportListAsync(
            new ImportListViewModel(_lists, plan, _sources, _services.Dialogs, _services.Browser, _timeProvider, _logger),
            cancellationToken);
}
