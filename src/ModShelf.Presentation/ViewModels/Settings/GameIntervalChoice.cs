namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// How often a game's watched mods are checked for updates: with the other games, or on an interval of its own.
/// </summary>
/// <param name="Interval">The game's own interval; null for every game's.</param>
/// <param name="Label">What the choice says, such as "Every day".</param>
public sealed record GameIntervalChoice(TimeSpan? Interval, string Label)
{
    public override string ToString() => Label;
}
