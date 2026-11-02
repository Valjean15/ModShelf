using System.Globalization;

using Avalonia.Data.Converters;

namespace ModShelf.Desktop.Views;

/// <summary>
/// How many cards fit side by side in a width: as many as are at least <see cref="MinCardWidth"/> wide with
/// <see cref="Spacing"/> between them, and at least one.
/// </summary>
internal sealed class CardColumnsConverter : IValueConverter
{
    public const double MinCardWidth = 220;

    public const double Spacing = 16;

    public static CardColumnsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width && width > 0 ? Math.Max(1, (int)((width + Spacing) / (MinCardWidth + Spacing))) : 1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A number of columns only goes one way.");
}
