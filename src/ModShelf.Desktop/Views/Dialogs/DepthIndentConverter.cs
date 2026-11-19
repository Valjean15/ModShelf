using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// How far a row of a tree is indented, by how deep it is.
/// </summary>
internal sealed class DepthIndentConverter : IValueConverter
{
    // Each level's indent, in device-independent pixels.
    private const double Step = 18;

    public static DepthIndentConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int depth ? new Thickness(depth * Step, 0, 0, 0) : new Thickness(0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("An indent only goes one way.");
}
