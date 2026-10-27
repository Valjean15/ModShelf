using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Services;

public sealed class NavigationServiceTests
{
    private readonly NavigationService _navigation = new();
    private readonly List<Screen> _navigated = [];

    public NavigationServiceTests()
    {
        _navigation.Navigated += (_, screen) => _navigated.Add(screen);
    }

    [Fact]
    public void StartsOnTheLibrary()
    {
        Assert.Equal(Screen.Library, _navigation.Current);
    }

    [Fact]
    public void GoesToAScreenAndSaysSo()
    {
        _navigation.NavigateTo(Screen.Downloads);
        _navigation.NavigateTo(Screen.Settings);

        Assert.Equal(Screen.Settings, _navigation.Current);
        Assert.Equal([Screen.Downloads, Screen.Settings], _navigated);
    }

    [Fact]
    public void GoingToTheScreenShownDoesNothing()
    {
        _navigation.NavigateTo(Screen.Library);

        Assert.Empty(_navigated);
    }

    [Fact]
    public void RefusesAScreenThatDoesntExist()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _navigation.NavigateTo((Screen)42));

        Assert.Equal(Screen.Library, _navigation.Current);
    }
}
