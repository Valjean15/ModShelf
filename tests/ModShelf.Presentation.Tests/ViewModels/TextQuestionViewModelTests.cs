using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class TextQuestionViewModelTests
{
    private static readonly Uri ApiKeyPage = new("https://next.nexusmods.com/settings/api-keys");

    private static readonly TextQuestion Question = new("Link Nexus Mods", "Paste your personal API key.", "API key", "Link")
    {
        IsSecret = true,
        HelpPage = ApiKeyPage,
        HelpPageLabel = "Open the API key page",
    };

    private readonly ScriptedBrowser _browser = new();

    [Fact]
    public void AsksTheQuestionAsGiven()
    {
        TextQuestionViewModel asked = new(Question, _browser);

        Assert.Equal(
            ("Link Nexus Mods", "Paste your personal API key.", "API key", "Link", "Cancel", true, true, "Open the API key page"),
            (asked.Title, asked.Message, asked.Hint, asked.ConfirmLabel, asked.CancelLabel, asked.IsSecret, asked.HasHelpPage, asked.HelpPageLabel));
        Assert.Equal(string.Empty, asked.Text);
        Assert.Null(asked.HelpPageProblem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void APageWithoutALabelIsntOffered(string? label)
        => Assert.False(new TextQuestionViewModel(Question with { HelpPageLabel = label }, _browser).HasHelpPage);

    [Fact]
    public async Task OpensTheHelpPage()
    {
        TextQuestionViewModel asked = new(Question, _browser);

        await asked.OpenHelpPageCommand.ExecuteAsync(null);

        Assert.Equal([ApiKeyPage], _browser.Opened);
        Assert.Null(asked.HelpPageProblem);
    }

    [Fact]
    public async Task ShowsThePagesAddressWhenNoBrowserOpens()
    {
        _browser.CanOpen = false;
        TextQuestionViewModel asked = new(Question, _browser);

        await asked.OpenHelpPageCommand.ExecuteAsync(null);

        Assert.Equal($"Your browser didn't open. Open this page by hand:{Environment.NewLine}{ApiKeyPage.AbsoluteUri}", asked.HelpPageProblem);
    }

    [Fact]
    public async Task ShowsThePagesAddressWhenTheLauncherRefusesIt()
    {
        _browser.Failure = new ArgumentException("Only http and https pages open.");
        TextQuestionViewModel asked = new(Question, _browser);

        await asked.OpenHelpPageCommand.ExecuteAsync(null);

        Assert.EndsWith(ApiKeyPage.AbsoluteUri, asked.HelpPageProblem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpeningItAgainClearsTheAddress()
    {
        _browser.CanOpen = false;
        TextQuestionViewModel asked = new(Question, _browser);
        await asked.OpenHelpPageCommand.ExecuteAsync(null);

        _browser.CanOpen = true;
        await asked.OpenHelpPageCommand.ExecuteAsync(null);

        Assert.Null(asked.HelpPageProblem);
    }
}
