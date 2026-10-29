using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Asks on the terminal for the link that completes a handoff: opens the page where the user starts the download,
/// and reads the link the site hands over. It writes to <see cref="CliEnvironment.Prompts"/>, so the person typing sees
/// it even when the output goes into a file. Each read runs off the command's thread, so the command keeps following
/// the download, and Ctrl+C ends it, while the user is busy on the site.
/// </summary>
/// <remarks>
/// The link can carry a key, so nothing here ever writes it back out.
/// </remarks>
internal sealed class HandoffPrompt(CliEnvironment environment, IBrowserLauncher browser)
{
    private bool _askedBefore;

    /// <summary>
    /// Opens <paramref name="page"/>, says what to do there, and starts reading the link.
    /// </summary>
    /// <returns>The line the user types next, or null when the input ends.</returns>
    public async Task<Task<string?>> AskAsync(Uri page, CancellationToken cancellationToken)
    {
        bool opened = await browser.TryOpenAsync(page, cancellationToken).ConfigureAwait(false);
        environment.Prompts.WriteLine(_askedBefore
            ? "The site didn't take that link; it may have expired. Start the download on the site again."
            : "The site wants this download started on its website.");
        environment.Prompts.WriteLine(opened
            ? "Its page opened in your browser. If it didn't, open this one:"
            : "Open this page in your browser:");
        environment.Prompts.WriteLine(page.AbsoluteUri);
        environment.Prompts.WriteLine(
            "Start the download there with the download for mod managers. The site then hands your browser a link (for "
            + "Nexus Mods, one starting with nxm://). Paste that link here and press Enter. Press Ctrl+C to stop; the "
            + "download keeps waiting.");

        _askedBefore = true;
        return Read();
    }

    /// <summary>
    /// Says why the last line wasn't used, and reads another.
    /// </summary>
    public Task<string?> AskAgain(string reason)
    {
        environment.Prompts.WriteLine(reason);
        return Read();
    }

    /// <summary>
    /// Console reads block, and ignore cancellation even in their async forms, so this one isn't awaited by anything
    /// that has to end: an abandoned read ends with the process.
    /// </summary>
    private Task<string?> Read()
    {
        environment.Prompts.Write("Link: ");
        return Task.Run(environment.Input.ReadLine, CancellationToken.None);
    }
}
