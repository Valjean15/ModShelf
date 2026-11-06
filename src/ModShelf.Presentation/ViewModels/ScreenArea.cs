namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The part of a screen a window can take, without the taskbar or the dock, in the screens' pixels, and how many of them
/// make one of the app's units.
/// </summary>
public sealed record ScreenArea(int X, int Y, int Width, int Height, double Scaling, bool IsPrimary)
{
    /// <summary>
    /// The area's width in the app's units.
    /// </summary>
    public double UnitWidth => Width / Scaling;

    /// <summary>
    /// The area's height in the app's units.
    /// </summary>
    public double UnitHeight => Height / Scaling;
}
