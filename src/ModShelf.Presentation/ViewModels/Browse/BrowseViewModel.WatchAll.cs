using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Watching in the library, for updates, every mod of the game the account follows on its site, as its tracked mods or
/// subscriptions are listed: those the library doesn't watch yet, one after the other.
/// </summary>
public sealed partial class BrowseViewModel
{
    /// <summary>
    /// Whether the followed mods listed have some the library doesn't watch, while Watch all isn't watching them.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WatchAllCommand))]
    public partial bool CanWatchAll { get; private set; }

    /// <summary>
    /// Such as "Watch all (5)".
    /// </summary>
    [ObservableProperty]
    public partial string WatchAllText { get; private set; } = Text.Format(Strings.BrowseWatchAll, 0);

    /// <summary>
    /// Whether Watch all is watching the mods.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWatchingAll { get; private set; }

    /// <summary>
    /// The followed mods of the game listed that the library doesn't watch.
    /// </summary>
    private ModRef[] Unwatched
        => IsShowingTracked ? [.. _trackedToShow.Where(mod => _inLibrary.GetValueOrDefault(mod) is not { IsWatched: true })] : [];

    /// <summary>
    /// Watches each followed mod of the game that the library doesn't, in the order listed, asking its site for its
    /// details; then says how many it watched, and how many it couldn't, with why.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWatchAll))]
    private async Task WatchAllAsync(CancellationToken cancellationToken)
    {
        ModRef[] mods = Unwatched;
        if (mods.Length == 0
            || !await QuotaChecks.GoOnAsync(_services.Sources, _services.Dialogs, _services.TimeProvider, mods.Select(mod => (mod.Game.Source, 1)), cancellationToken))
        {
            return;
        }

        IsWatchingAll = true;
        ShowWatchAll();
        int watched = 0;
        List<string> problems = [];
        try
        {
            foreach (ModRef mod in mods)
            {
                try
                {
                    LibraryMod now = await _services.Library.WatchAsync(mod, cancellationToken);
                    _inLibrary[mod] = now;
                    watched++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (SiteProblems.IsSiteProblem(exception) || exception is ArgumentException)
                {
                    problems.Add(exception.Message);
                }
                catch (Exception exception)
                {
                    LogWatchFailed(_logger, exception, mod.Id, mod.Game.Source.Value);
                    problems.Add(Strings.SomethingWentWrong);
                }
            }
        }
        finally
        {
            IsWatchingAll = false;
            string done = watched == 1 ? Strings.BrowseWatchedAllOne : Text.Format(Strings.BrowseWatchedAll, watched);
            SelectionNote = problems.Count == 0 ? done : $"{done} {Text.Format(Strings.BrowseWatchAllFailed, problems.Count, problems[0])}";
            IsSelectionNoteAProblem = problems.Count > 0;
            ShowWatchAll();
        }
    }

    partial void OnIsShowingTrackedChanged(bool value) => ShowWatchAll();

    /// <summary>
    /// Shows whether Watch all has mods to watch, and how many.
    /// </summary>
    private void ShowWatchAll()
    {
        int unwatched = Unwatched.Length;
        WatchAllText = Text.Format(Strings.BrowseWatchAll, unwatched);
        CanWatchAll = unwatched > 0 && !IsWatchingAll;
    }
}
