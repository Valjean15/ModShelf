using Avalonia;
using Avalonia.Controls;

using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop.Views;

/// <summary>
/// Cards side by side in a scroll viewer, in rows the scroll viewer builds only while they show: set
/// <c>CardColumns.For</c> on it to the view model's <see cref="CardRows"/>, and its rows get as many cards as fit its
/// width, as many as are at least <see cref="MinCardWidth"/> wide with <see cref="Spacing"/> between them, and at least
/// one, counted again when its width changes.
/// </summary>
internal static class CardColumns
{
    public const double MinCardWidth = 220;

    public const double Spacing = 16;

    public static readonly AttachedProperty<CardRows?> ForProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, CardRows?>("For", typeof(CardColumns));

    static CardColumns()
    {
        ForProperty.Changed.AddClassHandler<ScrollViewer>(OnForChanged);
    }

    public static CardRows? GetFor(ScrollViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return viewer.GetValue(ForProperty);
    }

    public static void SetFor(ScrollViewer viewer, CardRows? rows)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        viewer.SetValue(ForProperty, rows);
    }

    /// <summary>
    /// How many cards fit side by side in <paramref name="width"/>.
    /// </summary>
    public static int Fitting(double width)
        => width > 0 ? Math.Max(1, (int)((width + Spacing) / (MinCardWidth + Spacing))) : 1;

    private static void OnForChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs change)
    {
        viewer.SizeChanged -= OnSizeChanged;
        if (change.NewValue is CardRows rows)
        {
            viewer.SizeChanged += OnSizeChanged;
            rows.Columns = Fitting(viewer.Bounds.Width);
        }
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs changed)
    {
        if (sender is ScrollViewer viewer && GetFor(viewer) is { } rows)
        {
            rows.Columns = Fitting(changed.NewSize.Width);
        }
    }
}
