using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Library;
using ModShelf.Domain.Common;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// Where the library is, and moving it elsewhere.
/// </summary>
public sealed partial class SettingsViewModel
{
    private static string MessageOf(Exception exception)
        => exception is ArgumentException refused ? refused.MessageForUser() : exception.Message;

    private bool CanChangeRoot() => IsLoaded && !IsMoving;

    private bool CanUseDefaultRoot() => CanChangeRoot() && !IsDefaultRoot;

    /// <summary>
    /// Moves the library to a folder the user picks.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeRoot))]
    private async Task ChooseRootAsync(CancellationToken cancellationToken)
    {
        if (await _dialogs.PickFolderAsync(Strings.SettingsChooseFolderTitle, cancellationToken) is { } folder)
        {
            await ChangeRootAsync(folder, cancellationToken);
        }
    }

    /// <summary>
    /// Moves the library back to the default folder.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseDefaultRoot))]
    private Task UseDefaultRootAsync(CancellationToken cancellationToken) => ChangeRootAsync(null, cancellationToken);

    /// <summary>
    /// Makes <paramref name="folder"/> the library's root, or the default with null. A library with files moves them
    /// there, once the user confirms.
    /// </summary>
    private async Task ChangeRootAsync(string? folder, CancellationToken cancellationToken)
    {
        string target = folder ?? _settings.DefaultLibraryRoot;
        if (FilePaths.Same(target, LibraryRoot))
        {
            return;
        }

        RootNote = null;
        IsRootNoteAProblem = false;
        string? missing = null;
        try
        {
            int files = (await _library.ListAsync(cancellationToken)).Sum(mod => mod.Files.Count);
            if (files == 0)
            {
                await _settings.SetLibraryRootAsync(folder, cancellationToken);
            }
            else
            {
                string message = files == 1
                    ? Text.Format(Strings.SettingsMoveMessageOne, LibraryRoot, target)
                    : Text.Format(Strings.SettingsMoveMessage, files, LibraryRoot, target);
                if (!await _dialogs.ConfirmAsync(Strings.SettingsMoveTitle, message, Strings.SettingsMoveConfirm, cancellationToken))
                {
                    return;
                }

                IsMoving = true;
                LibraryMove move = await _mover.MoveAsync(target, cancellationToken);
                missing = move.Missing switch
                {
                    0 => null,
                    1 => Strings.SettingsMissingOne,
                    int count => Text.Format(Strings.SettingsMissing, count),
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing; a move that stopped part-way finishes when it's asked for again.
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or InvalidDataException)
        {
            // Such as downloads into the library that aren't finished, or a file that couldn't be moved.
            RootNote = MessageOf(exception);
            IsRootNoteAProblem = true;
        }
        catch (Exception exception)
        {
            LogMoveFailed(_logger, exception);
            RootNote = Strings.SomethingWentWrong;
            IsRootNoteAProblem = true;
        }
        finally
        {
            IsMoving = false;
        }

        // Whatever happened, the root shown is the one stored.
        string? readProblem = await ReloadAsync(CancellationToken.None);
        if (!IsRootNoteAProblem)
        {
            RootNote = missing is null
                ? Text.Format(Strings.SettingsLibraryIsIn, LibraryRoot)
                : $"{Text.Format(Strings.SettingsLibraryIsIn, LibraryRoot)} {missing}";
        }

        Problem = readProblem;
    }
}
