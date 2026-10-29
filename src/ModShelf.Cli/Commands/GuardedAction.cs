using System.CommandLine;
using System.CommandLine.Invocation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Cli.Hosting;
using ModShelf.Infrastructure.Logging;
using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Runs a command's action, and ends what it leaves unhandled in a line rather than a stack trace: a library database
/// that can't be opened exits <see cref="ExitCodes.Failed"/> with what's wrong; Ctrl+C,
/// <see cref="ExitCodes.Interrupted"/> with "Stopped."; and anything else, <see cref="ExitCodes.Unexpected"/>, with the
/// details in the log.
/// </summary>
internal sealed partial class GuardedAction(AsynchronousCommandLineAction action, CliEnvironment environment) : AsynchronousCommandLineAction
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
        ArgumentNullException.ThrowIfNull(parseResult);

        try
        {
            return await action.InvokeAsync(parseResult, cancellationToken).ConfigureAwait(false);
        }
        catch (LibraryDatabaseUnavailableException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("Stopped.").ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
        catch (Exception exception)
        {
            string? logs = Log(parseResult.CommandResult.Command, exception);
            string what = exception.Message.ReplaceLineEndings(" ").Trim();
            string where = logs is null ? string.Empty : $" The details are in the log in {logs}.";
            await environment.Error.WriteLineAsync($"Something went wrong that ModShelf didn't expect, so it stopped: {what}{where}").ConfigureAwait(false);
            return ExitCodes.Unexpected;
        }
    }

    /// <summary>
    /// The command as typed, without its arguments, such as <c>library list</c>.
    /// </summary>
    private static string CommandLine(Command command)
    {
        List<string> names = [];
        for (Command? current = command; current is not null and not RootCommand; current = current.Parents.OfType<Command>().FirstOrDefault())
        {
            names.Insert(0, current.Name);
        }

        return string.Join(' ', names);
    }

    /// <summary>
    /// Logs what went wrong, with the details, through a host built for it, since the command's own host is gone.
    /// </summary>
    /// <returns>The log files' folder; null when even this host can't be built.</returns>
    private string? Log(Command command, Exception exception)
    {
        try
        {
            using IHost host = environment.BuildHost(false);
            ILogger<GuardedAction> logger = host.Services.GetRequiredService<ILogger<GuardedAction>>();
            string typed = CommandLine(command);
            LogUnexpected(logger, exception, typed);
            return host.Services.GetService<LogFolder>()?.Path;
        }
        catch (Exception unbuildable) when (unbuildable is not OutOfMemoryException)
        {
            return null;
        }
    }
}
