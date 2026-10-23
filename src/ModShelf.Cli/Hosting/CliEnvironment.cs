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
    public static CliEnvironment ForConsole() => new(
        Console.In,
        Console.Out,
        Console.Error,
        InputIsTerminal: !Console.IsInputRedirected,
        OutputIsTerminal: !Console.IsOutputRedirected,
        runDownloads => CliHost.Build(runDownloads));
}
