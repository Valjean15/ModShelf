using System.CommandLine;

using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Every command the CLI has. Each command builds its own host through the environment, so DI, configuration and
/// logging are wired as in the desktop app.
/// </summary>
internal static class CliCommands
{
    public static RootCommand Create(CliEnvironment environment)
        => new("ModShelf from the command line: download files, look mods up, look at the download queue and link accounts.")
        {
            DownloadCommand.Create(environment),
            ModCommand.Create(environment),
            QueueCommand.Create(environment),
            AccountCommand.Create(environment),
        };
}
