using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using ModShelf.Presentation.ViewModels.QuickSearch;

namespace ModShelf.Desktop.Views.QuickSearch;

/// <summary>
/// A quick search row's icon, from Styles/Icons.axaml: a clock for a recent search, the settings' for a setting, and a
/// screen's own for a screen. Games and mods show their colour instead.
/// </summary>
internal sealed class QuickSearchIconConverter : IValueConverter
{
    public static QuickSearchIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? key = value switch
        {
            QuickSearchEntryViewModel { Kind: QuickSearchKind.Recent } => "RecentIcon",
            QuickSearchEntryViewModel { Kind: QuickSearchKind.Setting } => "SettingsIcon",
            QuickSearchEntryViewModel { Kind: QuickSearchKind.Screen, Screen: { } screen } => $"{screen}Icon",
            _ => null,
        };
        return key is not null && Avalonia.Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out object? icon) && icon is Geometry geometry
                ? geometry
                : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A row's icon only goes one way.");
}
