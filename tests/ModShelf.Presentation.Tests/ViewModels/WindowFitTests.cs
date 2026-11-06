using ModShelf.Application.State;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The window comes back fully visible on a screen that's there: as it was when it fits, moved in from the edges, no
/// larger than its screen, and centred on the main screen when its screen is gone.
/// </summary>
public sealed class WindowFitTests
{
    // A 1920×1080 main screen with a 40-pixel taskbar, and a 2560×1440 one at 150% to its right.
    private static readonly ScreenArea Main = new(0, 0, 1920, 1040, 1, IsPrimary: true);
    private static readonly ScreenArea Right = new(1920, 0, 2560, 1400, 1.5, IsPrimary: false);

    [Fact]
    public void AWindowThatFitsStaysAsItWas()
    {
        WindowPlacement placement = new(100, 50, 1200, 700, maximized: true);

        Assert.Equal(placement, WindowFit.Fit(placement, [Main, Right], 640, 400, 0, 0));
    }

    [Fact]
    public void AWindowOnTheSecondScreenStaysThereInItsScaling()
    {
        WindowPlacement placement = new(2000, 100, 1200, 800, maximized: false);

        Assert.Equal(placement, WindowFit.Fit(placement, [Main, Right], 640, 400, 0, 0));
    }

    [Fact]
    public void AScreenThatsGoneSendsItToTheMainScreenCentred()
    {
        WindowPlacement placement = new(2000, 100, 1200, 800, maximized: true);

        Assert.Equal(new WindowPlacement(360, 120, 1200, 800, maximized: true), WindowFit.Fit(placement, [Main], 640, 400, 0, 0));
    }

    [Fact]
    public void AWindowPartlyOffItsScreenIsMovedIn()
    {
        Assert.Equal(new WindowPlacement(720, 0, 1200, 700, maximized: false), WindowFit.Fit(new WindowPlacement(900, -30, 1200, 700, false), [Main], 640, 400, 0, 0));
        Assert.Equal(new WindowPlacement(0, 340, 1200, 700, maximized: false), WindowFit.Fit(new WindowPlacement(-500, 600, 1200, 700, false), [Main], 640, 400, 0, 0));
    }

    [Fact]
    public void AWindowLargerThanItsScreenIsMadeToFit()
    {
        ScreenArea small = new(0, 0, 1366, 728, 1, IsPrimary: true);

        Assert.Equal(new WindowPlacement(0, 0, 1366, 728, maximized: false), WindowFit.Fit(new WindowPlacement(10, 10, 1920, 1040, false), [small], 640, 400, 0, 0));
    }

    [Fact]
    public void AScaledScreenTakesTheSizeInItsUnits()
    {
        // 1400 pixels at 150% are 933⅓ units.
        WindowPlacement fitted = WindowFit.Fit(new WindowPlacement(2000, 0, 1200, 1000, false), [Main, Right], 640, 400, 0, 0)!;

        Assert.Equal((1200, 1400 / 1.5), (fitted.Width, fitted.Height));
        Assert.Equal((2000, 0), (fitted.X, fitted.Y));
    }

    [Fact]
    public void TheFrameStaysOnTheScreenToo()
    {
        // A title bar of 30 units and borders of 8 on each side, on the 150% screen.
        WindowPlacement fitted = WindowFit.Fit(new WindowPlacement(4000, 1200, 2000, 1000, false), [Main, Right], 640, 400, 16, 38)!;

        Assert.Equal((2560 / 1.5 - 16, 1400 / 1.5 - 38), (fitted.Width, fitted.Height));
        Assert.Equal((1920, 0), (fitted.X, fitted.Y));
        Assert.Equal(new WindowPlacement(704, 482, 1200, 520, false), WindowFit.Fit(new WindowPlacement(900, 800, 1200, 520, false), [Main], 640, 400, 16, 38));
    }

    [Fact]
    public void AScreenSmallerThanTheSmallestWindowStillHoldsIt()
    {
        ScreenArea tiny = new(0, 0, 600, 380, 1, IsPrimary: true);

        Assert.Equal(new WindowPlacement(0, 0, 600, 380, false), WindowFit.Fit(new WindowPlacement(0, 0, 800, 500, false), [tiny], 640, 400, 0, 0));
    }

    [Fact]
    public void AWindowSmallerThanTheSmallestIsMadeTheSmallest()
    {
        Assert.Equal(new WindowPlacement(10, 10, 640, 400, false), WindowFit.Fit(new WindowPlacement(10, 10, 300, 200, false), [Main], 640, 400, 0, 0));
    }

    [Fact]
    public void OnTheScreenItOverlapsMost()
    {
        // Mostly on the right screen: kept there, moved in from its left edge.
        WindowPlacement fitted = WindowFit.Fit(new WindowPlacement(1800, 100, 1000, 600, false), [Main, Right], 640, 400, 0, 0)!;

        Assert.Equal((1920, 100), (fitted.X, fitted.Y));
    }

    [Fact]
    public void TheOverlapIsTakenInEachScreensScaling()
    {
        // 400 units are 400 pixels wide on the main screen, of which 320 are on it, and 600 on the 150% one, of which 280
        // are there: as the window is half again as high there too, that's where most of it is.
        WindowPlacement fitted = WindowFit.Fit(new WindowPlacement(1600, 100, 400, 600, false), [Main, Right], 300, 300, 0, 0)!;

        Assert.Equal(new WindowPlacement(1920, 100, 400, 600, false), fitted);
    }

    [Fact]
    public void NothingRememberedOrNoScreenKnownPlacesItAsANewWindow()
    {
        Assert.Null(WindowFit.Fit(null, [Main], 640, 400, 0, 0));
        Assert.Null(WindowFit.Fit(new WindowPlacement(0, 0, 800, 600, false), [], 640, 400, 0, 0));
        Assert.Null(WindowFit.Fit(new WindowPlacement(0, 0, 800, 600, false), [new ScreenArea(0, 0, 0, 0, 1, true)], 640, 400, 0, 0));
    }

    [Fact]
    public void WithoutAMainScreenTheFirstTakesIt()
    {
        ScreenArea first = Main with { IsPrimary = false };

        Assert.Equal(new WindowPlacement(360, 120, 1200, 800, false), WindowFit.Fit(new WindowPlacement(9000, 9000, 1200, 800, false), [first, Right with { X = 5000 }], 640, 400, 0, 0));
    }

    [Fact]
    public void TheMainScreenTakesItWhereverItsListed()
    {
        Assert.Equal(new WindowPlacement(360, 120, 1200, 800, false), WindowFit.Fit(new WindowPlacement(9000, 9000, 1200, 800, false), [Right, Main], 640, 400, 0, 0));
    }
}
