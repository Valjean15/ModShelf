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
        { new LibraryViewModel(), Screen.Library, "Library" },
        { new UpdatesViewModel(), Screen.Updates, "Updates" },
        { new BrowseViewModel(), Screen.Browse, "Browse" },
        { new DownloadsViewModel(), Screen.Downloads, "Downloads" },
        { new AccountsViewModel(), Screen.Accounts, "Accounts" },
        { new SettingsViewModel(), Screen.Settings, "Settings" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void EachScreenHasItsTitle(ScreenViewModel viewModel, Screen screen, string title)
    {
        Assert.Equal((screen, title), (viewModel.Screen, viewModel.Title));
    }
}
