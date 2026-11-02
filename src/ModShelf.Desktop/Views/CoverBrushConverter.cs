using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ModShelf.Desktop.Views;

/// <summary>
/// A game's cover until it has a picture: a muted colour of the game's hue, which its initial reads on in white in light
/// and dark themes alike.
/// </summary>
internal sealed class CoverBrushConverter : IValueConverter
{
    public static CoverBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int hue ? new SolidColorBrush(new HslColor(1, hue, 0.28, 0.4).ToRgb()) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A colour only goes one way.");
}
