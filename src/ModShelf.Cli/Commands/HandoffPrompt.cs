using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Asks on the terminal for what completes a download waiting on a site's page: opens the page, and reads the link the
/// site hands over, or the path of the file the browser downloaded there, which ModShelf also takes by itself from the
/// browser's downloads folder (ADR 0091). It writes to <see cref="CliEnvironment.Prompts"/>, so the person typing sees
/// it even when the output goes into a file. Each read runs off the command's thread, so the command keeps following
/// the download, and Ctrl+C ends it, while the user is busy on the site.
/// </summary>
/// <remarks>
/// The link can carry a key, so nothing here ever writes it back out.
/// </remarks>
/// <param name="handsOverLinks">Whether the site's page hands a link over, as Nexus Mods' does; otherwise only a file.</param>
/// <param name="browserFolder">The folder the browser saves downloads to, which ModShelf watches.</param>
internal sealed class HandoffPrompt(CliEnvironment environment, IBrowserLauncher browser, bool handsOverLinks, string browserFolder)
{
    private bool _askedBefore;

    /// <summary>
    /// Whether the site's page hands a link over; otherwise only the file the browser downloads.
    /// </summary>
    public bool HandsOverLinks => handsOverLinks;

    /// <summary>
    /// Opens <paramref name="page"/>, says what to do there, and starts reading the link.
    /// </summary>
    /// <returns>The line the user types next, or null when the input ends.</returns>
    public async Task<Task<string?>> AskAsync(Uri page, CancellationToken cancellationToken)
    {
        bool opened = await browser.TryOpenAsync(page, cancellationToken).ConfigureAwait(false);
        environment.Prompts.WriteLine((_askedBefore, handsOverLinks) switch
        {
            (true, true) => "The site didn't take that link; it may have expired. Start the download on the site again.",
            (true, false) => "The file wasn't taken. Download it from its page again.",
            (false, true) => "The site wants this download started on its website.",
            (false, false) => "The file is on a page, which only your browser downloads it from.",
        });
        environment.Prompts.WriteLine(opened
            ? "Its page opened in your browser. If it didn't, open this one:"
            : "Open this page in your browser:");
        environment.Prompts.WriteLine(page.AbsoluteUri);
        environment.Prompts.WriteLine(handsOverLinks
            ? "Start the download there with the download for mod managers. The site then hands your browser a link (for "
                + "Nexus Mods, one starting with nxm://). Paste that link here and press Enter. If your browser downloads the "
                + $"file instead, ModShelf takes it from {browserFolder}, or type its path here. Press Ctrl+C to stop; the "
                + "download keeps waiting."
            : $"Download the file there. ModShelf takes it from {browserFolder} once your browser has saved it, or type its "
                + "path here and press Enter. Press Ctrl+C to stop; the download keeps waiting.");

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
        environment.Prompts.Write(handsOverLinks ? "Link: " : "Path: ");
        return Task.Run(environment.Input.ReadLine, CancellationToken.None);
    }
}
