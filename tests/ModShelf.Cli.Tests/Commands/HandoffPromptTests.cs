using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// Where the handoff's page and prompt go: to the output, or, when a person types while the output goes into a file,
/// to the error stream, which still shows on their terminal.
/// </summary>
public sealed class HandoffPromptTests : IDisposable
{
    private static readonly Uri Page = new("https://handoff.example/files/1/download");

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _browser.Dispose();
    }

    [Fact]
    public async Task APersonTypingWithTheOutputInAFileStillSeesWhatToDo()
    {
        await AskAsync(inputIsTerminal: true, outputIsTerminal: false);

        Assert.Empty(_output.ToString());
        Assert.Contains(Page.AbsoluteUri, _error.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("Link: ", _error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task OtherwiseItsInTheOutput(bool inputIsTerminal, bool outputIsTerminal)
    {
        await AskAsync(inputIsTerminal, outputIsTerminal);

        Assert.Empty(_error.ToString());
        Assert.Contains(Page.AbsoluteUri, _output.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("Link: ", _output.ToString(), StringComparison.Ordinal);
    }

    private async Task AskAsync(bool inputIsTerminal, bool outputIsTerminal)
    {
        CliEnvironment environment = new(
            new StringReader(string.Empty),
            _output,
            _error,
            inputIsTerminal,
            outputIsTerminal,
            _ => throw new InvalidOperationException("The prompt builds no host."));

        Task<string?> read = await new HandoffPrompt(environment, _browser).AskAsync(Page, TestContext.Current.CancellationToken);

        Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}
