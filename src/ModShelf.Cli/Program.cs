using System.CommandLine;

using ModShelf.Cli.Commands;

// Headless host for exercising the download engine end to end. Each command builds its own generic host
// (see CliHost), so DI, configuration and logging are wired as in the desktop app.
RootCommand root = new("ModShelf from the command line: download files and look at the download queue.")
{
    DownloadCommand.Create(),
    QueueCommand.Create(),
};

// On Ctrl+C the command cancels its token, then stops the engine so the running download is saved for later;
// give that time before System.CommandLine ends the process.
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(10) });
