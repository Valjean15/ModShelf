using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// A row's weight in a tree of an archive: a text file that can be read, such as a readme, stands out.
/// </summary>
internal sealed class TextWeightConverter : IValueConverter
{
    public static TextWeightConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.SemiBold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A weight only goes one way.");
}
