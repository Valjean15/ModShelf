namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// A line of what importing a list would do, such as its mods, or the files to download.
/// </summary>
public sealed record ImportListFact(string Label, string Value);
