using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The screens' names, from the resources, and that each is the screen it says.
/// </summary>
public sealed class ScreenTitlesTests
{
    public static TheoryData<ScreenViewModel, Screen, string> Screens => new()
    {
        { new LibraryHarness().Screen, Screen.Library, "Library" },
        { new UpdatesHarness().Screen, Screen.Updates, "Updates" },
        { new BrowseHarness().Screen, Screen.Browse, "Browse" },
        { new DownloadsHarness().Screen, Screen.Downloads, "Downloads" },
        { new AccountsHarness().Screen, Screen.Accounts, "Accounts" },
        { new SettingsHarness().Screen, Screen.Settings, "Settings" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void EachScreenHasItsTitle(ScreenViewModel viewModel, Screen screen, string title)
    {
        Assert.Equal((screen, title), (viewModel.Screen, viewModel.Title));
    }
}
