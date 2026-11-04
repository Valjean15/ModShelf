namespace ModShelf.Application.State;

/// <summary>
/// Where the desktop app's window was: its top-left corner, in the screens' pixels, its size, in the app's units, and
/// whether it was maximized. A maximized window's corner and size are those it goes back to.
/// </summary>
public sealed record WindowPlacement
{
    /// <exception cref="ArgumentOutOfRangeException">The width or the height isn't a number above 0.</exception>
    public WindowPlacement(int x, int y, double width, double height, bool maximized)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be a number above 0.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be a number above 0.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        Maximized = maximized;
    }

    public int X { get; }

    public int Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool Maximized { get; }
}
