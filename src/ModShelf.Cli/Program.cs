using System.CommandLine;

using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;

// Headless host for exercising the download engine end to end.
RootCommand root = CliCommands.Create(CliEnvironment.ForConsole());

// On Ctrl+C the command cancels its token, then stops the engine so the running download is saved for later;
// give that time before System.CommandLine ends the process.
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(10) });
