namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// One line of a mod's details on its page, such as "Category" and "Gameplay".
/// </summary>
public sealed record ModFact(string Label, string Value);
