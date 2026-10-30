using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// How often to check the watched mods for updates, as the Settings screen offers it.
/// </summary>
public sealed record IntervalChoice
{
    internal IntervalChoice(TimeSpan interval)
    {
        Interval = interval;
        string label = interval.Ticks switch
        {
            _ when interval == TimeSpan.FromDays(1) => Strings.IntervalEveryDay,
            _ when interval == TimeSpan.FromHours(1) => Strings.IntervalEveryHour,
            long ticks when ticks % TimeSpan.TicksPerDay == 0 => Text.Format(Strings.IntervalEveryDays, ticks / TimeSpan.TicksPerDay),
            long ticks when ticks % TimeSpan.TicksPerHour == 0 => Text.Format(Strings.IntervalEveryHours, ticks / TimeSpan.TicksPerHour),
            _ => Text.Format(Strings.IntervalEveryMinutes, (long)interval.TotalMinutes),
        };
        Label = interval == UpdateInterval.Default ? Text.Format(Strings.IntervalDefault, label) : label;
    }

    public TimeSpan Interval { get; }

    /// <summary>
    /// Such as "Every 6 hours (the default)".
    /// </summary>
    public string Label { get; }

    public override string ToString() => Label;
}
