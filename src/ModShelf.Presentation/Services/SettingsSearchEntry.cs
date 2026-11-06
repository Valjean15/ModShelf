using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Services;

/// <summary>
/// A part of the Settings screen, such as Previews in General: its category, the category's name, its heading, and the
/// words it says, which a search looks in.
/// </summary>
public sealed record SettingsSearchEntry(SettingsCategory Category, string CategoryName, string Heading, string Words);
