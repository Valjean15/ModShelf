namespace ModShelf.Application.Lists;

/// <summary>
/// Something a list needs from outside its mods' sites, such as a collection's script extender: ModShelf names it and
/// its page, and the user gets it.
/// </summary>
/// <param name="PageUrl">Where it's found; null when the list doesn't say.</param>
/// <param name="Optional">Whether the list marks it optional.</param>
public sealed record ListedElsewhere(string Name, Uri? PageUrl, bool Optional);
