namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// A day of the library's recent activity, with what happened on it, the newest first.
/// </summary>
/// <param name="Heading">Such as "Today" or "Monday, November 2, 2026".</param>
public sealed record ActivityDayViewModel(string Heading, IReadOnlyList<ActivityEntryViewModel> Entries);
