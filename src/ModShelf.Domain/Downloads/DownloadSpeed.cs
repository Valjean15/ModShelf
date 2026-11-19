using System.Globalization;

namespace ModShelf.Domain.Downloads;

/// <summary>
/// A download speed, such as the most ModShelf's downloads take together, written as kilobytes or megabytes a second,
/// such as <c>512KB</c> or <c>2MB</c>, in powers of 1024 as file managers show sizes (ADR 0105).
/// </summary>
public static class DownloadSpeed
{
    private const long Kilobyte = 1024;
    private const long Megabyte = 1024 * Kilobyte;

    /// <summary>
    /// The lowest limit, 16 KB a second: a download any slower would seem stuck.
    /// </summary>
    public const long Minimum = 16 * Kilobyte;

    /// <summary>
    /// The highest limit, 10,000 MB a second, far above any connection's speed.
    /// </summary>
    public const long Maximum = 10_000 * Megabyte;

    /// <summary>
    /// Reads a speed as <see cref="Format"/> writes it: a number, whole or with a decimal point, then <c>KB</c> or
    /// <c>MB</c>, with <c>/s</c> after it or not, such as <c>512KB</c>, <c>1.5 MB</c> or <c>2MB/s</c>. Case and spaces
    /// don't matter. A fraction of a kilobyte is dropped.
    /// </summary>
    /// <returns>The speed in bytes a second.</returns>
    /// <exception cref="ArgumentException">The text isn't a speed from <see cref="Minimum"/> to <see cref="Maximum"/>.</exception>
    public static long Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string speed = text.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (speed.EndsWith("/S", StringComparison.Ordinal))
        {
            speed = speed[..^2];
        }

        long? unit = speed.EndsWith("MB", StringComparison.Ordinal) ? Megabyte
            : speed.EndsWith("KB", StringComparison.Ordinal) ? Kilobyte
            : null;
        if (unit is { } size
            && decimal.TryParse(speed[..^2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal count)
            && count <= Maximum / Kilobyte)
        {
            long bytes = (long)(count * size) / Kilobyte * Kilobyte;
            if (bytes is >= Minimum and <= Maximum)
            {
                return bytes;
            }
        }

        throw new ArgumentException(
            $"'{text}' isn't a speed. Use kilobytes or megabytes a second, such as 512KB or 2MB, from {Format(Minimum)} to {Format(Maximum)}.",
            nameof(text));
    }

    /// <summary>
    /// The speed in whole megabytes when it's a whole number of them, such as <c>2MB</c>, and in kilobytes otherwise,
    /// such as <c>1536KB</c>; a part of a kilobyte is dropped.
    /// </summary>
    public static string Format(long bytesPerSecond)
        => bytesPerSecond > 0 && bytesPerSecond % Megabyte == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond / Megabyte}MB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond / Kilobyte}KB");
}
