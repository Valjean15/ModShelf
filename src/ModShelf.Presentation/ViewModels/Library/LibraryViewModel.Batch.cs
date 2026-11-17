using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Library;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Watching, unwatching and removing the selected mods at once, as each mod's own menu does for one: their site isn't
/// asked, unwatching and removing can be undone together from the undo bar, and what couldn't be done is said once,
/// mod by mod. The mods done are unselected; the others stay selected.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>
    /// Such as "Watch (2)": how many of the selected mods aren't watched.
    /// </summary>
    [ObservableProperty]
    public partial string WatchSelectedText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether some of the selected mods aren't watched, while nothing else is done to the selection.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WatchSelectedCommand))]
    public partial bool CanWatchSelected { get; private set; }

    /// <summary>
    /// Such as "Unwatch (3)": how many of the selected mods are watched.
    /// </summary>
    [ObservableProperty]
    public partial string UnwatchSelectedText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether some of the selected mods are watched, while nothing else is done to the selection.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnwatchSelectedCommand))]
    public partial bool CanUnwatchSelected { get; private set; }

    /// <summary>
    /// Such as "Remove (5)": how many mods are selected.
    /// </summary>
    [ObservableProperty]
    public partial string RemoveSelectedText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether mods are selected, while nothing else is done to the selection.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial bool CanRemoveSelected { get; private set; }

    /// <summary>
    /// Whether the selected mods are being watched, unwatched or removed.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChangingSelected { get; private set; }

    /// <summary>
    /// Watches the selected mods that aren't watched, so they're checked for updates.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWatchSelected))]
    private Task WatchSelectedAsync(CancellationToken cancellationToken)
        => ChangeSelectedAsync(
            mod => !mod.IsWatched,
            (mod, token) => _services.Library.SetWatchedAsync(mod.Mod, true, token),
            undone: null,
            Strings.LibraryWatchSelectedFailed,
            cancellationToken);

    /// <summary>
    /// Stops watching the selected mods that are watched, offering to undo it; one without files leaves the library.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnwatchSelected))]
    private Task UnwatchSelectedAsync(CancellationToken cancellationToken)
        => ChangeSelectedAsync(
            mod => mod.IsWatched,
            (mod, token) => _services.Library.SetWatchedAsync(mod.Mod, false, token),
            (Strings.UndoUnwatched, Strings.UndoUnwatchedMany),
            Strings.LibraryUnwatchSelectedFailed,
            cancellationToken);

    /// <summary>
    /// Takes the selected mods out of the library, offering to undo it. Their files stay where they are.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private Task RemoveSelectedAsync(CancellationToken cancellationToken)
        => ChangeSelectedAsync(
            _ => true,
            (mod, token) => _services.Library.ForgetAsync(mod.Mod, token),
            (Strings.UndoRemoved, Strings.UndoRemovedMany),
            Strings.LibraryRemoveSelectedFailed,
            cancellationToken);

    /// <summary>
    /// Changes the selected mods <paramref name="applies"/> to, one after the other, then unselects them, offers to undo
    /// what changed, and says what couldn't be done.
    /// </summary>
    /// <param name="undone">What the undo bar says for one mod and for several; null for a change it doesn't offer.</param>
    /// <param name="failedTitle">The heading of what couldn't be done, such as "Some mods weren't removed".</param>
    private async Task ChangeSelectedAsync(
        Func<LibraryModViewModel, bool> applies,
        Func<LibraryModViewModel, CancellationToken, Task<LibraryChange>> change,
        (string One, string Many)? undone,
        string failedTitle,
        CancellationToken cancellationToken)
    {
        LibraryModViewModel[] mods = [.. GameMods.Where(mod => mod.IsSelected && applies(mod))];
        if (mods.Length == 0)
        {
            return;
        }

        IsChangingSelected = true;
        ShowSelection();
        List<LibraryChange> changes = [];
        List<LibraryModViewModel> done = [];
        List<string> problems = [];
        try
        {
            foreach (LibraryModViewModel mod in mods)
            {
                try
                {
                    LibraryChange changed = await change(mod, cancellationToken);
                    done.Add(mod);
                    if (!ReferenceEquals(changed.Before, changed.After))
                    {
                        changes.Add(changed);
                    }
                }
                catch (KeyNotFoundException)
                {
                    // It left the library meanwhile.
                    done.Add(mod);
                }
                catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
                {
                    // Such as a download of one of its files that isn't finished, or settings that can't be read.
                    problems.Add(Text.Format(Strings.LibrarySelectedProblem, mod.Name, exception.Message));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogSelectedChangeFailed(_logger, exception, mod.Mod.Id, mod.Mod.Game.Key);
                    problems.Add(Text.Format(Strings.LibrarySelectedProblem, mod.Name, Strings.SomethingWentWrong));
                }
            }
        }
        finally
        {
            IsChangingSelected = false;
            Unselect(done);
        }

        if (undone is { } said && changes.Count > 0)
        {
            string name = mods.First(mod => mod.Mod == changes[0].Mod).Name;
            _services.Undo.Offer(changes.Count == 1 ? Text.Format(said.One, name) : Text.Format(said.Many, changes.Count), changes);
        }

        if (problems.Count > 0)
        {
            await _services.Dialogs.ShowMessageAsync(failedTitle, string.Join(Environment.NewLine, problems), cancellationToken);
        }
    }

    /// <summary>
    /// Says what the watch, unwatch and remove buttons would do to the selected mods.
    /// </summary>
    private void ShowSelectedChanges(LibraryModViewModel[] selected)
    {
        int watched = selected.Count(mod => mod.IsWatched);
        WatchSelectedText = Text.Format(Strings.LibraryWatchSelected, selected.Length - watched);
        UnwatchSelectedText = Text.Format(Strings.LibraryUnwatchSelected, watched);
        RemoveSelectedText = Text.Format(Strings.LibraryRemoveSelected, selected.Length);
        CanWatchSelected = !IsChangingSelected && selected.Length > watched;
        CanUnwatchSelected = !IsChangingSelected && watched > 0;
        CanRemoveSelected = !IsChangingSelected && selected.Length > 0;
    }
}
