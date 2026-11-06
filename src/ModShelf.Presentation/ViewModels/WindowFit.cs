using ModShelf.Application.State;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// Brings a window back where it was, fully visible on a screen that's there now: on the screen it overlaps most, moved
/// in from the edges, and no larger than that screen, its title bar and borders included; centred on the main screen when
/// it overlaps none, as when its screen is gone. A window's size is its inside's; its corner is its frame's.
/// </summary>
public static class WindowFit
{
    /// <param name="placement">Where the window was; null for nowhere remembered.</param>
    /// <param name="screens">The screens there are now.</param>
    /// <param name="minWidth">The window's smallest width, in the app's units, unless the screen is narrower.</param>
    /// <param name="minHeight">The window's smallest height, in the app's units, unless the screen is lower.</param>
    /// <param name="frameWidth">What the window's borders add to its width, in the app's units.</param>
    /// <param name="frameHeight">What its title bar and borders add to its height, in the app's units.</param>
    /// <returns>Where the window goes; null to place it as a new one, as with nothing remembered or no screen known.</returns>
    public static WindowPlacement? Fit(
        WindowPlacement? placement, IReadOnlyList<ScreenArea> screens, double minWidth, double minHeight, double frameWidth, double frameHeight)
    {
        ArgumentNullException.ThrowIfNull(screens);

        ScreenArea[] usable = [.. screens.Where(screen => screen.Width > 0 && screen.Height > 0 && screen.Scaling > 0)];
        if (placement is null || usable.Length == 0)
        {
            return null;
        }

        ScreenArea? overlapped = usable
            .Select(screen => (Screen: screen, Overlap: Overlap(placement, screen, frameWidth, frameHeight)))
            .Where(candidate => candidate.Overlap > 0)
            .OrderByDescending(candidate => candidate.Overlap)
            .Select(candidate => candidate.Screen)
            .FirstOrDefault();
        ScreenArea area = overlapped ?? usable.FirstOrDefault(screen => screen.IsPrimary) ?? usable[0];

        double widest = Math.Max(area.UnitWidth - frameWidth, 1);
        double highest = Math.Max(area.UnitHeight - frameHeight, 1);
        double width = Math.Clamp(placement.Width, Math.Min(minWidth, widest), widest);
        double height = Math.Clamp(placement.Height, Math.Min(minHeight, highest), highest);
        int pixelWidth = Math.Min((int)Math.Ceiling((width + frameWidth) * area.Scaling), area.Width);
        int pixelHeight = Math.Min((int)Math.Ceiling((height + frameHeight) * area.Scaling), area.Height);
        (int x, int y) = overlapped is null
            ? (area.X + ((area.Width - pixelWidth) / 2), area.Y + ((area.Height - pixelHeight) / 2))
            : (Math.Clamp(placement.X, area.X, area.X + area.Width - pixelWidth), Math.Clamp(placement.Y, area.Y, area.Y + area.Height - pixelHeight));
        return new WindowPlacement(x, y, width, height, placement.Maximized);
    }

    /// <summary>
    /// How many of the screen's pixels the window covers, its size and frame taken in the screen's scaling.
    /// </summary>
    private static long Overlap(WindowPlacement placement, ScreenArea screen, double frameWidth, double frameHeight)
    {
        long left = Math.Max(placement.X, screen.X);
        long top = Math.Max(placement.Y, screen.Y);
        long right = Math.Min(placement.X + (long)Math.Ceiling((placement.Width + frameWidth) * screen.Scaling), (long)screen.X + screen.Width);
        long bottom = Math.Min(placement.Y + (long)Math.Ceiling((placement.Height + frameHeight) * screen.Scaling), (long)screen.Y + screen.Height);
        return right > left && bottom > top ? (right - left) * (bottom - top) : 0;
    }
}
