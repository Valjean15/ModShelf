using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Views;

/// <summary>
/// A screen's icon, from Styles/Icons.axaml: <c>LibraryIcon</c> for <see cref="Screen.Library"/>, and so on.
/// </summary>
internal sealed class ScreenIconConverter : IValueConverter
{
    public static ScreenIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Screen screen && Avalonia.Application.Current is { } app
            && app.TryGetResource($"{screen}Icon", app.ActualThemeVariant, out object? icon) && icon is Geometry geometry
                ? geometry
                : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A screen's icon only goes one way.");
}
