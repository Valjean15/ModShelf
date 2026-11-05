namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One thing moving a game's files would do, such as "Moving" and "211 files, 18.2 GB".
/// </summary>
public sealed record GameMoveFact(string Label, string Value);
