using System.CommandLine;

using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;

// Headless host for exercising the engine end to end: downloads, the queue and linked accounts.
RootCommand root = CliCommands.Create(CliEnvironment.ForConsole());

// On Ctrl+C the command cancels its token, then stops the engine so the running download is saved for later;
// give that time before System.CommandLine ends the process.
return await CliCommands.InvokeAsync(
    root.Parse(args),
    new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(10) },
    CancellationToken.None);
