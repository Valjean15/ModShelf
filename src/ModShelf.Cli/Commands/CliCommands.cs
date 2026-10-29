using System.CommandLine;
using System.CommandLine.Invocation;

using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Every command the CLI has. Each command builds its own host through the environment, so DI, configuration and
/// logging are wired as in the desktop app.
/// </summary>
internal static class CliCommands
{
    public static RootCommand Create(CliEnvironment environment)
    {
        RootCommand root = new("ModShelf from the command line: download files, look mods up, keep a library of mods, look at the download queue, link accounts and change settings.")
        {
            DownloadCommand.Create(environment),
            ModCommand.Create(environment),
            LibraryCommand.Create(environment),
            QueueCommand.Create(environment),
            AccountCommand.Create(environment),
            SettingsCommand.Create(environment),
        };
        GuardedAction.GuardAll(root, environment);
        return root;
    }

    /// <summary>
    /// Runs the command <paramref name="parsed"/> names, as <c>Program.cs</c> does: arguments the command line can't
    /// read exit <see cref="ExitCodes.BadInput"/>, as arguments a command refuses do, rather than System.CommandLine's
    /// 1, which ModShelf keeps for what failed.
    /// </summary>
    public static async Task<int> InvokeAsync(ParseResult parsed, InvocationConfiguration invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        int exitCode = await parsed.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        return parsed.Action is ParseErrorAction ? ExitCodes.BadInput : exitCode;
    }
}
