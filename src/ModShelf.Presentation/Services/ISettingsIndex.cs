namespace ModShelf.Presentation.Services;

/// <summary>
/// What the Settings screen has, for quick search to find: each part of it, by its category and heading, with the words
/// it says.
/// </summary>
public interface ISettingsIndex
{
    IReadOnlyList<SettingsSearchEntry> Entries { get; }
}
