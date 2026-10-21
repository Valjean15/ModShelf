using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Sources.Abstractions.Tests.Downloads;

public sealed class HandoffRequiredTests
{
    [Fact]
    public void KeepsThePageToOpen()
    {
        Uri page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");

        Assert.Same(page, new HandoffRequired(page).PageUrl);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    public void RejectsUrlsThatMustNotBeOpenedInABrowser(string url)
    {
        Assert.Throws<ArgumentException>(() => new HandoffRequired(new Uri(url)));
    }

    [Fact]
    public void RejectsARelativeUrl()
    {
        Assert.Throws<ArgumentException>(() => new HandoffRequired(new Uri("mods/12604", UriKind.Relative)));
    }
}
