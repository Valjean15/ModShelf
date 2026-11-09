using System.CommandLine;
using System.CommandLine.Parsing;

namespace ModShelf.Cli.Commands;

/// <summary>
/// The command this run of the command line carries out, as its hosts' first log line says it (ADR 0081): the command's
/// words and the options given, such as <c>mcp --http --port</c>, never an argument's value, which can be a link with
/// a key. Set by <see cref="GuardedAction"/> for the command's own work, which builds its hosts.
/// </summary>
internal static class RunningCommand
{
    private static readonly AsyncLocal<string?> Typed = new();

    /// <summary>
    /// What the process runs, for <c>AddProcessStartLog</c>: <c>the command line, mcp --http</c>, or
    /// <c>the command line</c> outside a command.
    /// </summary>
    public static string Description => Typed.Value is { } typed ? $"the command line, {typed}" : "the command line";

    /// <summary>
    /// Says that the work that follows, in this async flow, carries out <paramref name="parseResult"/>'s command.
    /// </summary>
    public static void Set(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        Typed.Value = Of(parseResult);
    }

    /// <summary>
    /// The command as typed, its words and its options' names alone, such as <c>library rescan</c> or
    /// <c>mcp --http --port</c>.
    /// </summary>
    internal static string Of(ParseResult parseResult)
    {
        List<string> words = [];
        for (Command? current = parseResult.CommandResult.Command; current is not null and not RootCommand; current = current.Parents.OfType<Command>().FirstOrDefault())
        {
            words.Insert(0, current.Name);
        }

        words.AddRange(parseResult.CommandResult.Children
            .OfType<OptionResult>()
            .Where(option => !option.Implicit)
            .Select(option => option.Option.Name));
        return string.Join(' ', words);
    }
}
