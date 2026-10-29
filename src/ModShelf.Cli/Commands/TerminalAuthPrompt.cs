using System.Text;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Asks on the terminal for what linking an account needs, writing to <see cref="CliEnvironment.Prompts"/>. An API key
/// typed by a person isn't shown as it's typed; when the input is redirected, each answer is read as a line.
/// </summary>
internal sealed class TerminalAuthPrompt(CliEnvironment environment) : IAuthPrompt
{
    public Task<string> AskEmailAsync(SourceId source, CancellationToken cancellationToken)
    {
        environment.Prompts.Write("Email address: ");
        return ReadLineAsync(cancellationToken);
    }

    public Task<string> AskApiKeyAsync(SourceId source, Uri? keyPage, CancellationToken cancellationToken)
    {
        if (keyPage is not null)
        {
            environment.Prompts.WriteLine($"Find your API key for {source} at {keyPage}");
        }

        // Only a person at a terminal types without it showing; typed into a pipe, the terminal shows it.
        if (environment.InputIsTerminal)
        {
            environment.Prompts.Write("API key (not shown as you type): ");
            return ReadHiddenAsync(cancellationToken);
        }

        environment.Prompts.Write("API key: ");
        return ReadLineAsync(cancellationToken);
    }

    public Task<string> AskEmailCodeAsync(SourceId source, string? sentTo, CancellationToken cancellationToken)
    {
        environment.Prompts.WriteLine(sentTo is null ? $"{source} is emailing you a code." : $"{source} is emailing a code to {sentTo}.");
        environment.Prompts.Write("Code: ");
        return ReadLineAsync(cancellationToken);
    }

    public void ShowBrowserStep(SourceId source, Uri authorizeUrl, bool browserOpened)
    {
        environment.Prompts.WriteLine(browserOpened
            ? $"Sign in to {source} in the page that opened in your browser, and approve ModShelf. If no page opened, open this one:"
            : $"Open this page in your browser, sign in to {source} and approve ModShelf:");
        environment.Prompts.WriteLine(authorizeUrl.AbsoluteUri);
        environment.Prompts.WriteLine("Waiting for your approval. Press Ctrl+C to stop.");
    }

    /// <summary>
    /// Console reads block, and ignore the cancellation token even in their async forms. Running the read off the
    /// command's thread lets Ctrl+C end the command while it waits; the abandoned read ends with the process.
    /// </summary>
    private static async Task<string> ReadOffThreadAsync(Func<string?> read, CancellationToken cancellationToken)
    {
        string? line = await Task.Run(read, CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            // The input ended, or, on Windows, Ctrl+C ended the read before it stopped the command: a moment tells which.
            await Task.Delay(CliEnvironment.InputEndGrace, cancellationToken).ConfigureAwait(false);
        }

        return line ?? string.Empty;
    }

    private Task<string> ReadLineAsync(CancellationToken cancellationToken)
        => ReadOffThreadAsync(environment.Input.ReadLine, cancellationToken);

    /// <summary>
    /// Reads keys without echoing them.
    /// </summary>
    private async Task<string> ReadHiddenAsync(CancellationToken cancellationToken)
    {
        string secret = await ReadOffThreadAsync(
                () =>
                {
                    StringBuilder typed = new();
                    while (true)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            return typed.ToString();
                        }

                        if (key.Key == ConsoleKey.Backspace)
                        {
                            typed.Length = Math.Max(0, typed.Length - 1);
                        }
                        else if (!char.IsControl(key.KeyChar))
                        {
                            typed.Append(key.KeyChar);
                        }
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        environment.Prompts.WriteLine();
        return secret;
    }
}
