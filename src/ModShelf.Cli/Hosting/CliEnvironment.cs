using Microsoft.Extensions.Hosting;

namespace ModShelf.Cli.Hosting;

/// <summary>
/// What the commands talk to: the terminal, and the host each command builds. <c>Program.cs</c> passes the real
/// console and <see cref="CliHost"/>; tests pass their own, so commands run in-process with fake sources.
/// </summary>
/// <param name="InputIsTerminal">Whether a person types the input, so a secret can be read without showing it.</param>
/// <param name="OutputIsTerminal">Whether a person reads the output, so progress can update in place.</param>
/// <param name="BuildHost">Builds the host for one command; the argument says whether queued downloads run.</param>
internal sealed record CliEnvironment(
    TextReader Input,
    TextWriter Output,
    TextWriter Error,
    bool InputIsTerminal,
    bool OutputIsTerminal,
    Func<bool, IHost> BuildHost)
{
    /// <summary>
    /// How long a read that ended at a prompt waits for Ctrl+C before it counts as the end of the input: on Windows,
    /// Ctrl+C ends the read before it stops the command.
    /// </summary>
    public static TimeSpan InputEndGrace { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Where to ask, and to say what to do before asking: the output, unless a person types while the output goes
    /// elsewhere, such as into a file; then the error stream, which still shows on their terminal.
    /// </summary>
    public TextWriter Prompts => InputIsTerminal && !OutputIsTerminal ? Error : Output;

    public static CliEnvironment ForConsole() => new(
        Console.In,
        Console.Out,
        Console.Error,
        InputIsTerminal: !Console.IsInputRedirected,
        OutputIsTerminal: !Console.IsOutputRedirected,
        runDownloads => CliHost.Build(runDownloads));
}
