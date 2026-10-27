using System.Globalization;

namespace ModShelf.Domain.Updates;

/// <summary>
/// How often the watched mods are checked for updates in the background, written as a number of minutes, hours or
/// days, such as <c>30m</c>, <c>6h</c> or <c>1d</c>.
/// </summary>
public static class UpdateInterval
{
    /// <summary>
    /// The interval when none is set.
    /// </summary>
    public static TimeSpan Default { get; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The shortest interval, so checks stay well within the sites' limits.
    /// </summary>
    public static TimeSpan Minimum { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest interval.
    /// </summary>
    public static TimeSpan Maximum { get; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Reads an interval as <see cref="Format"/> writes it. Case and surrounding spaces don't matter.
    /// </summary>
    /// <exception cref="ArgumentException">The text isn't an interval from <see cref="Minimum"/> to <see cref="Maximum"/>.</exception>
    public static TimeSpan Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string interval = text.Trim();
        if (interval.Length >= 2
            && int.TryParse(interval.AsSpan(0, interval.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int count)

            // No count beyond the longest interval in minutes is in range, whatever its unit.
            && count <= Maximum.TotalMinutes
            && char.ToLowerInvariant(interval[^1]) switch
            {
                'm' => TimeSpan.FromMinutes(count),
                'h' => TimeSpan.FromHours(count),
                'd' => TimeSpan.FromDays(count),
                _ => (TimeSpan?)null,
            } is { } parsed
            && parsed >= Minimum
            && parsed <= Maximum)
        {
            return parsed;
        }

        throw new ArgumentException(
            $"'{text}' isn't an interval. Use a number of minutes, hours or days, such as 30m, 6h or 1d, from {Format(Minimum)} to {Format(Maximum)}.",
            nameof(text));
    }

    /// <summary>
    /// The interval in the largest of days, hours or minutes that it's a whole number of, such as <c>6h</c> or
    /// <c>90m</c>.
    /// </summary>
    public static string Format(TimeSpan interval) => interval switch
    {
        { Ticks: > 0 } when interval.Ticks % TimeSpan.TicksPerDay == 0 => string.Create(CultureInfo.InvariantCulture, $"{interval.Ticks / TimeSpan.TicksPerDay}d"),
        { Ticks: > 0 } when interval.Ticks % TimeSpan.TicksPerHour == 0 => string.Create(CultureInfo.InvariantCulture, $"{interval.Ticks / TimeSpan.TicksPerHour}h"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(long)interval.TotalMinutes}m"),
    };
}
