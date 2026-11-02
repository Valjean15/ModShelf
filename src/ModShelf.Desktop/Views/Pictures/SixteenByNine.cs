using Avalonia;
using Avalonia.Controls;

namespace ModShelf.Desktop.Views.Pictures;

/// <summary>
/// Holds its child as wide as it's given and nine sixteenths of that high, as a card's picture is.
/// </summary>
internal sealed class SixteenByNine : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        Size size = new(width, width * 9 / 16);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = new(finalSize.Width, finalSize.Width * 9 / 16);
        Child?.Arrange(new Rect(size));
        return size;
    }
}
