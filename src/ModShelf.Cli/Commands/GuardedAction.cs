using System.CommandLine;
using System.CommandLine.Invocation;

using ModShelf.Cli.Hosting;
using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Runs a command's action, and says in a line what stopped it when that's something the user can act on wherever it
/// happens, such as a library database that can't be opened, rather than leaving a stack trace.
/// </summary>
internal sealed class GuardedAction(AsynchronousCommandLineAction action, CliEnvironment environment) : AsynchronousCommandLineAction
{
    /// <summary>
    /// Guards the action of <paramref name="command"/> and of every command under it.
    /// </summary>
    public static void GuardAll(Command command, CliEnvironment environment)
    {
        if (command.Action is AsynchronousCommandLineAction action)
        {
            command.Action = new GuardedAction(action, environment);
        }

        foreach (Command subcommand in command.Subcommands)
        {
            GuardAll(subcommand, environment);
        }
    }

    public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        try
        {
            return await action.InvokeAsync(parseResult, cancellationToken).ConfigureAwait(false);
        }
        catch (LibraryDatabaseUnavailableException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
    }
}
