using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Undo;

/// <summary>
/// The bar at the bottom of the window after a change to the library that can be undone, such as a mod removed from it:
/// what happened, with Undo, for <see cref="ShownFor"/>. One at a time: a newer change replaces the one offered. Undoing
/// puts the mod back as it was, through the library; what can't be undone says why there, for as long.
/// </summary>
public sealed partial class UndoBarViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long the bar shows: ten seconds.
    /// </summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(10);

    private readonly IModLibrary _library;
    private readonly IUiThread _uiThread;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UndoBarViewModel> _logger;
    private LibraryChange? _offered;
    private ITimer? _timer;
    private int _shown;

    public UndoBarViewModel(IModLibrary library, IUiThread uiThread, TimeProvider timeProvider, ILogger<UndoBarViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _uiThread = uiThread;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Whether the bar shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// What happened, such as "SkyUI removed from the library."; or why it couldn't be undone.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the bar offers Undo: while it shows a change, not why one couldn't be undone.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; private set; }

    /// <summary>
    /// The last undoing, for tests to wait on; it never throws.
    /// </summary>
    internal Task Undoing { get; private set; } = Task.CompletedTask;

    public void Dispose() => _timer?.Dispose();

    /// <summary>
    /// Shows what a change did, offering to undo it, in place of what the bar showed.
    /// </summary>
    /// <param name="said">Such as "SkyUI removed from the library."</param>
    /// <param name="change">The change, as the library returned it.</param>
    internal void Offer(string said, LibraryChange change)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(change);

        _offered = change;
        Show(said, canUndo: true);
    }

    /// <summary>
    /// Puts the mod back as it was before the change offered, closing the bar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync()
    {
        if (_offered is not { } change)
        {
            return Task.CompletedTask;
        }

        Hide();
        Undoing = UndoingAsync(change);
        return Undoing;
    }

    [RelayCommand]
    private void Close() => Hide();

    private async Task UndoingAsync(LibraryChange change)
    {
        try
        {
            await _library.RestoreAsync(change, CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            // Such as a mod removed, and in the library again since.
            Show(Text.Format(Strings.UndoFailed, exception.Message), canUndo: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUndoFailed(_logger, exception, change.Mod.Id, change.Mod.Game.Key);
            Show(Text.Format(Strings.UndoFailed, Strings.SomethingWentWrong), canUndo: false);
        }
    }

    private void Show(string message, bool canUndo)
    {
        int shown = ++_shown;
        Message = message;
        CanUndo = canUndo;
        IsShown = true;
        _timer?.Dispose();
        _timer = _timeProvider.CreateTimer(_ => _uiThread.Post(() => Expire(shown)), null, ShownFor, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Closes the bar once its time is up, unless it shows something newer.
    /// </summary>
    private void Expire(int shown)
    {
        if (shown == _shown)
        {
            Hide();
        }
    }

    private void Hide()
    {
        _shown++;
        _timer?.Dispose();
        _timer = null;
        _offered = null;
        CanUndo = false;
        IsShown = false;
    }
}
