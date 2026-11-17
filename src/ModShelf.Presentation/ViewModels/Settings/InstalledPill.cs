namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One site's game for an installed game in Settings › Games, as a small pill: the site's name, and the game's name when
/// it isn't the installed game's; whether it was chosen, chosen as none, or is one of several of that name.
/// </summary>
/// <param name="Text">Such as "Nexus Mods", or "Nexus Mods: Skyrim Special Edition".</param>
/// <param name="Tip">Everything said of it, such as "Nexus Mods: Skyrim Special Edition, chosen".</param>
public sealed record InstalledPill(string Text, string Tip, bool IsChosen, bool IsNone, bool IsShared);
