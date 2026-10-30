using ModShelf.Application.Settings;
using ModShelf.Domain.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Saving each change as it's made, one after another, and showing the settings as they're stored.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>
    /// Saves a choice the user made, after the ones before it, then says what went wrong, if anything. Choices shown
    /// from the settings aren't saved back.
    /// </summary>
    private void Save(Func<CancellationToken, Task> change)
    {
        if (!_showing)
        {
            _ = ChangeAsync(change, sayProblem: true);
        }
    }

    /// <summary>
    /// Saves a game's folder pattern, or every game's with a null game; a null pattern sets it back.
    /// </summary>
    /// <returns>What went wrong; null when it's saved.</returns>
    private Task<string?> SaveFoldersAsync(string? game, FolderPattern? pattern)
        => ChangeAsync(token => _settings.SetLibraryFoldersAsync(game, pattern, token), sayProblem: false);

    /// <summary>
    /// Makes a change once the changes before it are done, then shows the settings as stored.
    /// </summary>
    /// <param name="sayProblem">Whether to show what went wrong as <see cref="Problem"/>, rather than only return it.</param>
    /// <returns>What went wrong, changing or reading the settings; null for nothing.</returns>
    private Task<string?> ChangeAsync(Func<CancellationToken, Task> change, bool sayProblem)
    {
        Task<string?> changing = ChangeAfterAsync(_saving, change, sayProblem);
        _saving = changing;
        return changing;
    }

    private async Task<string?> ChangeAfterAsync(Task before, Func<CancellationToken, Task> change, bool sayProblem)
    {
        // An earlier change, which never throws.
        await before;

        string? problem = null;
        try
        {
            await change(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            // Such as settings that can't be read or saved.
            problem = MessageOf(exception);
        }
        catch (Exception exception)
        {
            LogSaveFailed(_logger, exception);
            problem = Strings.SomethingWentWrong;
        }

        // Whatever happened, what's shown is what's stored.
        string? readProblem = await ReloadAsync(CancellationToken.None);
        problem ??= readProblem;
        if (sayProblem)
        {
            Problem = problem;
        }

        return problem;
    }

    /// <summary>
    /// Reads the settings, and shows them.
    /// </summary>
    /// <returns>Why they couldn't be read; null when they're shown.</returns>
    private async Task<string?> ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Show(await _settings.GetAsync(cancellationToken));
            return null;
        }
        catch (InvalidDataException exception)
        {
            IsLoaded = false;
            return exception.Message;
        }
    }

    /// <summary>
    /// Shows the settings as stored, without saving them back, and applies their theme.
    /// </summary>
    private void Show(AppSettings settings)
    {
        _showing = true;
        try
        {
            _shown = settings;
            LibraryRoot = settings.LibraryRoot ?? _settings.DefaultLibraryRoot;
            IsDefaultRoot = FilePaths.Same(LibraryRoot, _settings.DefaultLibraryRoot);

            Folders.Show(settings.LibraryFolders ?? FolderPattern.Default, LibraryRoot);
            List<FolderPatternViewModel> games = [];
            foreach ((string game, FolderPattern pattern) in settings.GameFolders.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                FolderPatternViewModel row = GameFolders.FirstOrDefault(shown => string.Equals(shown.Game, game, StringComparison.OrdinalIgnoreCase))
                    ?? new FolderPatternViewModel(game, pattern, LibraryRoot, SaveFoldersAsync);
                row.Show(pattern, LibraryRoot);
                games.Add(row);
            }

            GameFolders.SyncWith(games);

            OldVersionRule rule = settings.OldVersions ?? OldVersionRule.Default;
            OldVersionAction = rule.Action;
            if (rule.Action == OldVersionAction.KeepLatest)
            {
                KeepCount = rule.Count;
            }

            TimeSpan interval = settings.UpdateInterval ?? UpdateInterval.Default;
            IntervalChoices.SyncWith(
            [
                .. OfferedIntervals.Append(interval).Distinct().Order()
                    .Select(offered => IntervalChoices.FirstOrDefault(choice => choice.Interval == offered) ?? new IntervalChoice(offered)),
            ]);
            SelectedInterval = IntervalChoices.First(choice => choice.Interval == interval);

            NotifyOfUpdates = settings.NotifyOfUpdates ?? true;
            Theme = settings.Theme ?? AppTheme.System;
            CloseToTray = settings.CloseToTray;
            IsLoaded = true;
        }
        finally
        {
            _showing = false;
        }
    }
}
