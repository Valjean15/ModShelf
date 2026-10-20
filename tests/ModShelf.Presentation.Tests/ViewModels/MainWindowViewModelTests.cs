using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void TitleIsTheProductNameFromResources()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal("ModShelf", viewModel.Title);
    }
}
