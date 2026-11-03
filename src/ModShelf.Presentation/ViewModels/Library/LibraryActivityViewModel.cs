using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The library's recent activity: what happened to its files over the last year, by day, the newest first, which a list
/// narrows to one kind; each opens its mod's page. It's read each time it opens.
/// </summary>
public sealed partial class LibraryActivityViewModel : ObservableObject
{
    /// <summary>
    /// The most it shows: the newest.
    /// </summary>
    public const int Limit = 1000;

    private readonly ILibraryHistory _history;
    private readonly Func<ModRef, CancellationToken, Task> _open;
    private readonly Func<GameRef, string> _gameName;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private IReadOnlyList<LibraryEvent> _events = [];

    internal LibraryActivityViewModel(
        ILibraryHistory history, Func<ModRef, CancellationToken, Task> open, Func<GameRef, string> gameName, TimeProvider timeProvider, ILogger logger)
    {
        _history = history;
        _open = open;
        _gameName = gameName;
        _timeProvider = timeProvider;
        _logger = logger;
        Kinds =
        [
            new(null, Strings.ActivityKindAll),
            new(LibraryEventKind.Downloaded, Strings.ActivityKindDownloaded),
            new(LibraryEventKind.Archived, Strings.ActivityKindArchived),
            new(LibraryEventKind.Deleted, Strings.ActivityKindDeleted),
            new(LibraryEventKind.Found, Strings.ActivityKindFound),
            new(LibraryEventKind.Removed, Strings.ActivityKindRemoved),
        ];
        SelectedKind = Kinds[0];
    }

    /// <summary>
    /// What it can be narrowed to: everything, or one kind.
    /// </summary>
    public IReadOnlyList<LibraryChoice<LibraryEventKind?>> Kinds { get; }

    [ObservableProperty]
    public partial LibraryChoice<LibraryEventKind?> SelectedKind { get; set; }

    /// <summary>
    /// The days something of the kind chosen happened, the newest first.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ActivityDayViewModel> Days { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// What's said in place of the days when there are none; null when there are.
    /// </summary>
    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    /// <summary>
    /// Why the history couldn't be read; null when it was.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// That only the newest <see cref="Limit"/> are shown; null when that's all there is.
    /// </summary>
    [ObservableProperty]
    public partial string? CappedText { get; private set; }

    /// <summary>
    /// Reads the history, and shows it.
    /// </summary>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        Problem = null;
        try
        {
            _events = await _history.ListAsync(null, Limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogCantRead(_logger, exception);
            _events = [];
            Problem = Strings.ActivityCantRead;
        }
        finally
        {
            IsLoading = false;
        }

        CappedText = _events.Count >= Limit ? Common.Text.Format(Strings.ActivityCapped, Limit) : null;
        Arrange();
    }

    partial void OnSelectedKindChanged(LibraryChoice<LibraryEventKind?> value) => Arrange();

    private void Arrange()
    {
        LibraryEventKind? kind = SelectedKind.Value;
        Days =
        [
            .. _events
                .Where(happened => kind is null || happened.Kind == kind)
                .GroupBy(happened => Dates.LocalDay(happened.At, _timeProvider))
                .Select(day => new ActivityDayViewModel(
                    Dates.DayHeading(day.Key, _timeProvider),
                    [.. day.Select(happened => new ActivityEntryViewModel(happened, _gameName(happened.Mod.Game), _timeProvider, _open))])),
        ];
        EmptyText = Days.Count > 0 || Problem is not null ? null
            : _events.Count == 0 ? Strings.ActivityEmpty
            : Strings.ActivityNothingOfKind;
    }
}
